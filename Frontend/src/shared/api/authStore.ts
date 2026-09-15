import { create } from 'zustand';
import type { UserProfile, MenuNode } from './types';

/**
 * The authenticated session.
 *
 * The access token lives **in this store only** - never in localStorage, never in a
 * non-HttpOnly cookie (spec §11.5). A store is module state: it dies with the tab and no
 * XSS payload can read it out of persistent storage after the fact.
 *
 * The refresh token is not here at all and could not be: it is an HttpOnly cookie that
 * JavaScript cannot read (spec A1 and ADR-004). All the SPA knows about it is when it
 * expires.
 */
interface AuthState {
  accessToken: string | null;
  /** Epoch milliseconds. Drives proactive refresh. */
  accessTokenExpiresAt: number | null;
  refreshTokenExpiresAt: number | null;
  user: UserProfile | null;
  menu: MenuNode[];
  /**
   * True once `/auth/me` has answered for this session. Separates "the role has no menus"
   * from "the menu has not arrived", which an empty `menu` alone cannot.
   */
  isMenuLoaded: boolean;
  /** False until the boot-time silent refresh has resolved one way or the other. */
  isBootstrapped: boolean;

  setSession: (session: {
    accessToken: string;
    accessTokenExpiresUtc: string;
    refreshTokenExpiresUtc: string;
    user: UserProfile;
  }) => void;
  setMenu: (menu: MenuNode[]) => void;
  setUser: (user: UserProfile) => void;
  markBootstrapped: () => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  accessTokenExpiresAt: null,
  refreshTokenExpiresAt: null,
  user: null,
  menu: [],
  isMenuLoaded: false,
  isBootstrapped: false,

  setSession: (session) =>
    set({
      accessToken: session.accessToken,
      accessTokenExpiresAt: Date.parse(session.accessTokenExpiresUtc),
      refreshTokenExpiresAt: Date.parse(session.refreshTokenExpiresUtc),
      user: session.user,
    }),

  setMenu: (menu) => set({ menu, isMenuLoaded: true }),
  setUser: (user) => set({ user }),
  markBootstrapped: () => set({ isBootstrapped: true }),

  clear: () =>
    set({
      accessToken: null,
      accessTokenExpiresAt: null,
      refreshTokenExpiresAt: null,
      user: null,
      menu: [],
      isMenuLoaded: false,
    }),
}));

/** Non-reactive read, for the HTTP client which runs outside React. */
export const authSnapshot = () => useAuthStore.getState();

export const hasPermission = (permission: string): boolean =>
  useAuthStore.getState().user?.permissions.includes(permission) ?? false;

export const hasAnyPermission = (permissions: readonly string[]): boolean => {
  const granted = useAuthStore.getState().user?.permissions;

  return granted ? permissions.some((p) => granted.includes(p)) : false;
};
