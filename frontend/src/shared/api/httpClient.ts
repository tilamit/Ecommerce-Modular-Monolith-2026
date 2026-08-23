import { authSnapshot, useAuthStore } from './authStore';
import type { AuthResponse, ProblemDetails } from './types';

/**
 * Thrown for any non-2xx response. Carries the parsed ProblemDetails so callers can branch
 * on `problem.code` - a stable machine-readable string - rather than on a message.
 */
export class ApiError extends Error {
  // Declared as fields rather than constructor parameter properties: the TypeScript 6
  // template enables `erasableSyntaxOnly`, which forbids syntax that emits runtime code.
  readonly status: number;

  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);

    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get code(): string | undefined {
    return this.problem.code;
  }

  /** True when the session cannot be renewed and the SPA must send the user to login. */
  get isSessionExpired(): boolean {
    return this.status === 401 && this.problem.code === 'refresh_expired';
  }
}

const REFRESH_PATH = '/api/v1/auth/refresh';

/**
 * Refresh this long before the access token actually expires (spec §11.5). Covers clock
 * skew and the round trip, so a request is never sent with a token that dies in flight.
 */
const PROACTIVE_REFRESH_WINDOW_MS = 60_000;

/**
 * The single-flight promise.
 *
 * Module-level on purpose (spec §11.5): if ten requests 401 at once, they must produce
 * **one** refresh call and then all replay. A per-request promise would fire ten refreshes,
 * nine of which present an already-rotated token - which the backend correctly treats as
 * token reuse and responds to by revoking the entire family, logging the user out.
 *
 * So this variable is not an optimisation. Without it, concurrency looks like theft.
 */
let inFlightRefresh: Promise<boolean> | null = null;

/** Called when the session is unrecoverable, so the app can route to /login once. */
type SessionExpiredHandler = () => void;

let onSessionExpired: SessionExpiredHandler = () => {};

export const setSessionExpiredHandler = (handler: SessionExpiredHandler): void => {
  onSessionExpired = handler;
};

/** The SPA route that issued the request, for the audit trail's ScreenName (spec §6.6). */
const currentClientPage = (): string =>
  typeof window === 'undefined' ? 'unknown' : window.location.pathname;

const newCorrelationId = (): string =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2);

/**
 * Performs the refresh.
 *
 * No body and no Authorization header: the refresh token travels as an HttpOnly cookie, so
 * `credentials: 'include'` is what carries it (spec §7.3). `credentials` is also why CORS
 * on the API must name an explicit origin - `AllowAnyOrigin` is rejected with credentials.
 */
const performRefresh = async (): Promise<boolean> => {
  try {
    const response = await fetch(REFRESH_PATH, {
      method: 'POST',
      credentials: 'include',
      headers: { 'X-Correlation-Id': newCorrelationId() },
    });

    if (!response.ok) {
      return false;
    }

    const session = (await response.json()) as AuthResponse;
    useAuthStore.getState().setSession(session);

    return true;
  } catch {
    // A network failure is not a session failure. Returning false lets the caller surface
    // the original error rather than logging the user out over a dropped connection.
    return false;
  }
};

/** Coalesces concurrent refreshes into one in-flight call. */
const refreshOnce = (): Promise<boolean> => {
  inFlightRefresh ??= performRefresh().finally(() => {
    inFlightRefresh = null;
  });

  return inFlightRefresh;
};

/** Ends the session exactly once, however many requests discovered it at the same time. */
const endSession = (): void => {
  if (authSnapshot().accessToken === null && !authSnapshot().isBootstrapped) {
    return;
  }

  useAuthStore.getState().clear();
  onSessionExpired();
};

const parseProblem = async (response: Response): Promise<ProblemDetails> => {
  try {
    const body = (await response.json()) as ProblemDetails;

    return typeof body === 'object' && body !== null ? body : { status: response.status };
  } catch {
    return { status: response.status, title: response.statusText };
  }
};

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown;
  /** Set for endpoints that must never trigger a refresh, such as login. */
  skipAuth?: boolean;
}

/**
 * The single entry point for every API call.
 *
 * Order of business, per spec §11.5:
 * 1. proactively refresh if the access token expires within a minute;
 * 2. attach the bearer token, X-Client-Page and X-Correlation-Id;
 * 3. on a 401, refresh once (single-flight) and replay the request;
 * 4. on refresh failure, clear the session once and hand off to the router.
 */
export const apiRequest = async <T>(path: string, options: RequestOptions = {}): Promise<T> => {
  const { body, skipAuth = false, headers, ...rest } = options;

  // Guard against an infinite loop: /refresh must never trigger a refresh of its own.
  const isRefreshCall = path.startsWith(REFRESH_PATH);

  if (!skipAuth && !isRefreshCall) {
    const { accessToken, accessTokenExpiresAt } = authSnapshot();

    if (
      accessToken !== null &&
      accessTokenExpiresAt !== null &&
      accessTokenExpiresAt - Date.now() < PROACTIVE_REFRESH_WINDOW_MS
    ) {
      await refreshOnce();
    }
  }

  const send = async (): Promise<Response> => {
    const requestHeaders = new Headers(headers);

    requestHeaders.set('X-Client-Page', currentClientPage());
    requestHeaders.set('X-Correlation-Id', newCorrelationId());

    if (body !== undefined) {
      requestHeaders.set('Content-Type', 'application/json');
    }

    const token = authSnapshot().accessToken;

    if (!skipAuth && token !== null) {
      requestHeaders.set('Authorization', `Bearer ${token}`);
    }

    return fetch(path, {
      ...rest,
      headers: requestHeaders,
      // Always: the refresh cookie must travel, and CORS is configured for it (spec §11.5).
      credentials: 'include',
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  };

  let response = await send();

  // Reactive refresh. Only worth attempting when we had a token that the server rejected;
  // a 401 with no token at all means "not signed in", not "token expired".
  if (response.status === 401 && !skipAuth && !isRefreshCall && authSnapshot().accessToken !== null) {
    const refreshed = await refreshOnce();

    if (refreshed) {
      response = await send();
    } else {
      endSession();
    }
  }

  if (response.status === 204) {
    return undefined as T;
  }

  if (!response.ok) {
    const problem = await parseProblem(response);
    const error = new ApiError(response.status, problem);

    if (error.isSessionExpired) {
      endSession();
    }

    throw error;
  }

  // A 200 with an empty body is legitimate for some endpoints; treat it as undefined
  // rather than letting JSON.parse throw on an empty string.
  const text = await response.text();

  return (text.length === 0 ? undefined : JSON.parse(text)) as T;
};

export const api = {
  get: <T>(path: string, options?: RequestOptions) => apiRequest<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    apiRequest<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    apiRequest<T>(path, { ...options, method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    apiRequest<T>(path, { ...options, method: 'PATCH', body }),
  delete: <T>(path: string, options?: RequestOptions) =>
    apiRequest<T>(path, { ...options, method: 'DELETE' }),
};

/**
 * One silent refresh on app boot (spec §11.5).
 *
 * After a page reload the access token is gone - it only ever lived in memory - but the
 * HttpOnly cookie is not. This is what turns a refresh of the browser tab into a continued
 * session rather than a logout.
 */
export const bootstrapSession = async (): Promise<boolean> => {
  const refreshed = await refreshOnce();

  useAuthStore.getState().markBootstrapped();

  return refreshed;
};

/** Test seam: resets the single-flight promise between cases. */
export const resetRefreshStateForTests = (): void => {
  inFlightRefresh = null;
};
