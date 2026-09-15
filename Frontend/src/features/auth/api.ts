import { api } from '../../shared/api/httpClient';
import { useAuthStore } from '../../shared/api/authStore';
import type { AuthResponse, MeResponse } from '../../shared/api/types';
import { mergeCart } from '../cart';
import { useCartStore } from '../cart';

export interface LoginInput {
  email: string;
  password: string;
}

export interface RegisterInput {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
}

/**
 * Signs in and adopts the session.
 *
 * `skipAuth` is set because there is no session yet: without it the client would try to
 * refresh a token it does not have before every login attempt.
 */
export const login = async (input: LoginInput): Promise<AuthResponse> => {
  const session = await api.post<AuthResponse>('/api/v1/auth/login', input, { skipAuth: true });

  useAuthStore.getState().setSession(session);
  await mergeLocalCartIntoSession();

  return session;
};

export const register = async (input: RegisterInput): Promise<AuthResponse> => {
  const session = await api.post<AuthResponse>('/api/v1/auth/register', input, { skipAuth: true });

  useAuthStore.getState().setSession(session);
  await mergeLocalCartIntoSession();

  return session;
};

let meInFlight: Promise<MeResponse> | null = null;

/**
 * Loads the profile and the menu tree.
 *
 * Single-flight: the boot sequence and the sidebar can both ask at once after a reload, and
 * two callers should cost one request.
 */
export const fetchMe = (): Promise<MeResponse> => {
  meInFlight ??= api
    .get<MeResponse>('/api/v1/auth/me')
    .then((me) => {
      useAuthStore.getState().setUser(me.profile);
      useAuthStore.getState().setMenu(me.menu);

      return me;
    })
    .finally(() => {
      meInFlight = null;
    });

  return meInFlight;
};

export const logout = async (): Promise<void> => {
  try {
    await api.post<void>('/api/v1/auth/logout');
  } finally {
    // Clear locally even if the call failed: the user asked to be signed out and leaving
    // them apparently signed in because the network hiccuped is the wrong answer.
    useAuthStore.getState().clear();
  }
};

/**
 * Merges any unexpired localStorage cart into the server cart, then clears it
 * (spec §8.4 state 3: "clears localStorage on 2xx").
 *
 * Failure is deliberately swallowed. A failed merge must not fail the sign-in - the user is
 * authenticated either way and their local cart is still on disk to retry with. Clearing
 * only on success is what makes that retry safe.
 */
const mergeLocalCartIntoSession = async (): Promise<void> => {
  const { cart, clear } = useCartStore.getState();

  if (cart === null || cart.items.length === 0) {
    return;
  }

  try {
    await mergeCart(
      crypto.randomUUID(),
      cart.items.map((item) => ({ productId: item.productId, quantity: item.quantity })),
    );

    clear();
  } catch {
    // Left in place for the next attempt.
  }
};
