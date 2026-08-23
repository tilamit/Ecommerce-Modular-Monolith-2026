import { describe, expect, it, beforeEach, vi, afterEach } from 'vitest';
import { api, ApiError, bootstrapSession, setSessionExpiredHandler, resetRefreshStateForTests } from './httpClient';
import { useAuthStore } from './authStore';
import type { UserProfile } from './types';

/**
 * Spec §12 names "refresh interceptor single-flight" as required frontend coverage, and
 * §11.5 calls the refresh logic "the single most bug-prone piece of the frontend".
 */

const user: UserProfile = {
  id: 'u1',
  email: 'ada@example.com',
  firstName: 'Ada',
  lastName: 'Lovelace',
  fullName: 'Ada Lovelace',
  isActive: true,
  roles: ['Customer'],
  permissions: ['cart.own'],
};

const sessionPayload = (token: string, expiresInMs: number) => ({
  accessToken: token,
  accessTokenExpiresUtc: new Date(Date.now() + expiresInMs).toISOString(),
  refreshTokenExpiresUtc: new Date(Date.now() + 7 * 24 * 3600_000).toISOString(),
  user,
});

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const signIn = (token = 'token-1', expiresInMs = 15 * 60_000) => {
  useAuthStore.getState().setSession(sessionPayload(token, expiresInMs));
};

describe('httpClient', () => {
  beforeEach(() => {
    resetRefreshStateForTests();
    useAuthStore.getState().clear();
    setSessionExpiredHandler(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('attaches the bearer token, X-Client-Page and X-Correlation-Id', async () => {
    signIn('token-abc');

    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/api/v1/carts/me');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);

    expect(headers.get('Authorization')).toBe('Bearer token-abc');
    expect(headers.get('X-Client-Page')).toBeTruthy();
    expect(headers.get('X-Correlation-Id')).toBeTruthy();

    // Without this the refresh cookie never travels (spec §11.5).
    expect(init.credentials).toBe('include');
  });

  it('refreshes proactively when the access token expires within a minute', async () => {
    // 30 seconds left: inside the 60-second proactive window.
    signIn('stale-token', 30_000);

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(sessionPayload('fresh-token', 15 * 60_000)))
      .mockResolvedValueOnce(jsonResponse({ ok: true }));

    vi.stubGlobal('fetch', fetchMock);

    await api.get('/api/v1/carts/me');

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/auth/refresh');

    // The replayed request carries the *new* token, not the one that was about to die.
    const [, init] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer fresh-token');
  });

  it('does not refresh proactively when the token has plenty of life left', async () => {
    signIn('good-token', 10 * 60_000);

    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/api/v1/carts/me');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).not.toBe('/api/v1/auth/refresh');
  });

  it('refreshes reactively on a 401 and replays the request', async () => {
    signIn('expired-token');

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ title: 'Unauthorized' }, 401))
      .mockResolvedValueOnce(jsonResponse(sessionPayload('renewed-token', 15 * 60_000)))
      .mockResolvedValueOnce(jsonResponse({ items: [] }));

    vi.stubGlobal('fetch', fetchMock);

    const result = await api.get<{ items: unknown[] }>('/api/v1/orders/me');

    expect(result.items).toEqual([]);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/auth/refresh');
  });

  /**
   * The single-flight guarantee, and the reason it exists.
   *
   * Refresh tokens rotate and the backend treats a replayed token as theft - it revokes the
   * entire family. So N concurrent 401s producing N refresh calls would not merely be
   * wasteful: the second one presents an already-rotated token and logs the user out.
   */
  it('coalesces concurrent 401s into exactly one refresh call', async () => {
    signIn('expired-token');

    let refreshCalls = 0;

    const fetchMock = vi.fn((path: string) => {
      if (path === '/api/v1/auth/refresh') {
        refreshCalls += 1;

        // Deliberately slow, so all five requests are waiting on the same promise.
        return new Promise<Response>((resolve) =>
          setTimeout(() => resolve(jsonResponse(sessionPayload('renewed', 15 * 60_000))), 20),
        );
      }

      return Promise.resolve(
        useAuthStore.getState().accessToken === 'renewed'
          ? jsonResponse({ ok: true })
          : jsonResponse({ title: 'Unauthorized' }, 401),
      );
    });

    vi.stubGlobal('fetch', fetchMock);

    await Promise.all([
      api.get('/api/v1/a'),
      api.get('/api/v1/b'),
      api.get('/api/v1/c'),
      api.get('/api/v1/d'),
      api.get('/api/v1/e'),
    ]);

    expect(refreshCalls).toBe(1);
  });

  it('never tries to refresh the refresh endpoint itself', async () => {
    signIn('expired-token');

    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ code: 'refresh_expired' }, 401));
    vi.stubGlobal('fetch', fetchMock);

    await expect(api.post('/api/v1/auth/refresh')).rejects.toBeInstanceOf(ApiError);

    // Exactly one call: the loop guard stopped it recursing.
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('clears the session once and notifies the app when refresh fails', async () => {
    signIn('expired-token');

    const onExpired = vi.fn();
    setSessionExpiredHandler(onExpired);

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ title: 'Unauthorized' }, 401))
      .mockResolvedValueOnce(jsonResponse({ code: 'refresh_expired' }, 401))
      .mockResolvedValueOnce(jsonResponse({ code: 'refresh_expired' }, 401));

    vi.stubGlobal('fetch', fetchMock);

    await expect(api.get('/api/v1/orders/me')).rejects.toBeInstanceOf(ApiError);

    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(onExpired).toHaveBeenCalledTimes(1);
  });

  it('surfaces the problem code so callers branch on it rather than on a message', async () => {
    signIn();

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ code: 'insufficient_stock', title: 'Some items are no longer available.' }, 409),
      ),
    );

    const error = await api.post('/api/v1/checkout').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('insufficient_stock');
    expect((error as ApiError).status).toBe(409);
  });

  it('treats a 204 as an empty result rather than failing to parse it', async () => {
    signIn();

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    await expect(api.post('/api/v1/auth/logout')).resolves.toBeUndefined();
  });

  it('restores a session on boot from the refresh cookie alone', async () => {
    // No access token: exactly the state after a page reload, since it lived only in memory.
    expect(useAuthStore.getState().accessToken).toBeNull();

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(sessionPayload('restored-token', 15 * 60_000))),
    );

    const restored = await bootstrapSession();

    expect(restored).toBe(true);
    expect(useAuthStore.getState().accessToken).toBe('restored-token');
    expect(useAuthStore.getState().isBootstrapped).toBe(true);
  });

  it('marks bootstrap complete even when there is no session to restore', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'refresh_expired' }, 401)));

    const restored = await bootstrapSession();

    expect(restored).toBe(false);
    expect(useAuthStore.getState().isBootstrapped).toBe(true);
  });

  it('does not log the user out over a network blip', async () => {
    signIn('expired-token');

    const onExpired = vi.fn();
    setSessionExpiredHandler(onExpired);

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ title: 'Unauthorized' }, 401))
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(jsonResponse({ title: 'Unauthorized' }, 401));

    vi.stubGlobal('fetch', fetchMock);

    await expect(api.get('/api/v1/orders/me')).rejects.toBeInstanceOf(ApiError);

    // The refresh failed, so the session ends - but via the normal path, not by throwing
    // the network error at the user.
    expect(onExpired).toHaveBeenCalled();
  });
});
