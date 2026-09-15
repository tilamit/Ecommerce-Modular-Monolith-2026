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

  /**
   * True when the server recorded the request as abandoned (499) rather than failed.
   *
   * This is what a cancelled request looks like from the outside: a page navigation, a
   * superseded typeahead call, or React's development-mode double mount. Nothing is broken
   * and nothing will be fixed by showing an error, so the QueryClient retries this one
   * status among the 4xx range.
   */
  get isCancelled(): boolean {
    return this.status === CLIENT_CLOSED_REQUEST;
  }

  /**
   * True when the response carried no body where JSON was promised.
   *
   * Transient by nature and worth repeating for that reason. The cause is a request that
   * was joined to another request's work at some layer - a shared cache entry, a proxy that
   * coalesces - and inherited its abandonment. Nothing is wrong with the server and the
   * next attempt is answered normally, so this is retried rather than shown.
   */
  get isEmpty(): boolean {
    return this.problem.code === 'empty_response';
  }

  /**
   * True when nothing answered: the API is not listening, or a proxy in front of it could
   * not reach it.
   *
   * Worth telling apart from a failure, because the usual cause is a race rather than a
   * fault. Vite starts in under a second; a cold `dotnet run` applies migrations, seeds and
   * JITs first, so on the first run of the day the SPA is on screen and asking for products
   * well before the API is listening. The dev proxy answers 502 until it is and a request
   * that nobody answered is worth sending again - which is the one thing a hard error state
   * does not do.
   */
  get isUnreachable(): boolean {
    return this.status === SERVICE_UNREACHABLE || GATEWAY_STATUSES.has(this.status);
  }
}

/** Nginx's 499, which the API returns when it notices the caller has hung up. */
export const CLIENT_CLOSED_REQUEST = 499;

/**
 * Not a status any server sent - `fetch` itself rejected, so there was no response to read
 * a status from. Zero is what the platform uses for the same idea in `XMLHttpRequest`.
 */
export const SERVICE_UNREACHABLE = 0;

/** A proxy or gateway saying it could not reach the thing behind it. */
const GATEWAY_STATUSES = new Set([502, 503, 504]);

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
 * nine of which present an already-rotated token. The backend treats that as token reuse
 * and revokes the entire family, logging the user out - so this is a correctness
 * requirement rather than an optimisation.
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

/** True when the response said it was sending JSON, whether or not it then did. */
const declaresJson = (response: Response): boolean =>
  response.headers.get('Content-Type')?.toLowerCase().includes('json') === true;

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

  const isMultipart = typeof FormData !== 'undefined' && body instanceof FormData;

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

    // FormData sets its own Content-Type, including the multipart boundary the browser
    // generates. Setting it here would produce a header with no boundary and a body the
    // server cannot parse, so a file upload goes through this client untouched rather than
    // around it - keeping the token refresh, the correlation headers and the error mapping.
    if (body !== undefined && !isMultipart) {
      requestHeaders.set('Content-Type', 'application/json');
    }

    const token = authSnapshot().accessToken;

    if (!skipAuth && token !== null) {
      requestHeaders.set('Authorization', `Bearer ${token}`);
    }

    try {
      return await fetch(path, {
        ...rest,
        headers: requestHeaders,
        // Always: the refresh cookie must travel and CORS is configured for it (spec §11.5).
        credentials: 'include',
        body: body === undefined ? undefined : isMultipart ? (body as FormData) : JSON.stringify(body),
      });
    } catch (error) {
      // An abort is this app's own doing - React Query tearing a query down, a superseded
      // typeahead - and the caller relies on it rejecting. Rethrowing it unchanged is what
      // keeps a cancelled query from being retried as if the server had gone away.
      if (rest.signal?.aborted === true || (error instanceof DOMException && error.name === 'AbortError')) {
        throw error;
      }

      // Anything else here is a connection that never produced a response. Given a shape
      // callers can branch on, rather than a bare TypeError that only reads as "failed".
      throw new ApiError(SERVICE_UNREACHABLE, {
        status: SERVICE_UNREACHABLE,
        title: 'The server could not be reached.',
        detail: 'The API did not answer. It may still be starting up.',
        code: 'service_unreachable',
      });
    }
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

  const text = await response.text();

  if (text.length === 0) {
    // An empty body is legitimate for the endpoints that promise no content and is not
    // worth a JSON.parse on an empty string. But a response that announced JSON and then
    // sent none did not succeed, whatever its status line says - so raise it rather than
    // handing the caller `undefined` for a value the types say is always there. That is
    // the difference between one retried request and a page that throws while rendering.
    if (declaresJson(response)) {
      throw new ApiError(response.status, {
        status: response.status,
        title: 'The server returned an empty response.',
        detail: 'The response was announced as JSON but arrived with no content.',
        code: 'empty_response',
      });
    }

    return undefined as T;
  }

  return JSON.parse(text) as T;
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
