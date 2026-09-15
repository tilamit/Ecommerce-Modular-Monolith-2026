import { useEffect, useState, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { bootstrapSession, setSessionExpiredHandler, ApiError } from '../shared/api/httpClient';
import { useAuthStore } from '../shared/api/authStore';
import { useCartStore } from '../features/cart/cartStore';
import { fetchMe } from '../features/auth/api';
import { ToastProvider } from '../shared/components/ui/Toast';

/**
 * Retry budget for an API that is not listening yet.
 *
 * The API answers its first request about four seconds after starting; Vite serves the SPA
 * in under one, so on a first run the storefront asks for products before anything can
 * answer and the dev proxy returns 502 until it can.
 *
 * Polling rather than backing off, because the API comes up at an unpredictable moment and a
 * doubling delay tends to sleep through it. Fast at first, then every two seconds so an
 * absent backend is not hammered. Twenty-four attempts is a ~39-second budget, during which
 * the query stays `pending` and the page holds its skeleton.
 */
const UNREACHABLE_RETRIES = 24;

const UNREACHABLE_FAST_RETRIES = 6;

const UNREACHABLE_FAST_DELAY_MS = 500;

const UNREACHABLE_RETRY_DELAY_MS = 2_000;

/**
 * An empty body where JSON was promised. Unlike an absent API this needs nothing to change
 * before it succeeds, so it is retried immediately and only a few times: if it repeats, the
 * cause is not the race this is here for.
 */
const EMPTY_RETRIES = 3;

const EMPTY_RETRY_DELAY_MS = 250;

/** React Query's own default: double each time, capped at 30 seconds. */
const backoffDelay = (failureCount: number) => Math.min(1_000 * 2 ** failureCount, 30_000);

/**
 * Exported so the retry policy below can be tested directly. It is the part of this file
 * that decides whether a first run shows the catalogue or an error message and it is not
 * reachable through a rendered component without standing up the whole app.
 */
export const createQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: {
        // Spec §11.3: 30s for lists. Cart and orders override this to 0 at the call site.
        staleTime: 30_000,
        retry: (failureCount, error) => {
          if (error instanceof ApiError) {
            // Nothing answered - the API is not listening yet, or a proxy could not reach
            // it. Nothing has failed that a moment's patience will not fix.
            if (error.isUnreachable) {
              return failureCount < UNREACHABLE_RETRIES;
            }

            // 499 is the one 4xx worth repeating: nothing failed, the request was simply
            // abandoned - so send it again rather than showing "something went wrong" for
            // a page the user is still looking at.
            if (error.isCancelled) {
              return failureCount < 2;
            }

            // The server answered with nothing: the request was joined to another one's
            // work and inherited its abandonment and asking again is answered properly.
            // Left alone the body parses to `undefined` and surfaces as a render-time
            // crash rather than as a failed request.
            if (error.isEmpty) {
              return failureCount < EMPTY_RETRIES;
            }

            // Never retry any other client error: a 403 or a 404 will not become a 200,
            // and retrying an auth failure just burns through the rate limiter.
            if (error.status >= 400 && error.status < 500) {
              return false;
            }
          }

          return failureCount < 2;
        },
        retryDelay: (failureCount, error) => {
          if (!(error instanceof ApiError)) {
            return backoffDelay(failureCount);
          }

          // Nothing has to recover for this one to succeed, so there is nothing to wait
          // for beyond letting the request that was abandoned finish being abandoned.
          if (error.isEmpty) {
            return EMPTY_RETRY_DELAY_MS;
          }

          if (!error.isUnreachable) {
            return backoffDelay(failureCount);
          }

          return failureCount < UNREACHABLE_FAST_RETRIES
            ? UNREACHABLE_FAST_DELAY_MS
            : UNREACHABLE_RETRY_DELAY_MS;
        },
        refetchOnWindowFocus: false,
      },
    },
  });

/**
 * Runs once on boot.
 *
 * Two things have to happen before the app can render meaningfully: the localStorage cart
 * is hydrated (and swept for expiry) and one silent refresh attempts to restore a session
 * from the HttpOnly cookie - the access token itself is gone after a page reload, because
 * it only ever lived in memory (spec §11.5).
 */
const SessionBootstrap = ({ children }: { children: ReactNode }) => {
  const navigate = useNavigate();
  const isBootstrapped = useAuthStore((state) => state.isBootstrapped);
  const hydrateCart = useCartStore((state) => state.hydrate);
  const sweepCart = useCartStore((state) => state.sweep);

  useEffect(() => {
    setSessionExpiredHandler(() => {
      const returnUrl = `${window.location.pathname}${window.location.search}`;

      navigate(`/login?returnUrl=${encodeURIComponent(returnUrl)}`, { replace: true });
    });
  }, [navigate]);

  useEffect(() => {
    hydrateCart();

    void bootstrapSession().then(async (restored) => {
      if (restored) {
        // The refresh response carries the profile, but not the menu tree, so /me is what
        // populates the dynamic sidebar (spec §11.2).
        await fetchMe().catch(() => undefined);
      }
    });
  }, [hydrateCart]);

  // Spec §8.4: "a setInterval sweep every 60s handles the tab-left-open case."
  useEffect(() => {
    const timer = setInterval(() => sweepCart(), 60_000);

    return () => clearInterval(timer);
  }, [sweepCart]);

  if (!isBootstrapped) {
    return (
      <div className="flex min-h-dvh items-center justify-center" aria-busy="true">
        <p className="text-sm text-content-muted">Loading…</p>
      </div>
    );
  }

  return <>{children}</>;
};

export const AppProviders = ({ children }: { children: ReactNode }) => {
  // Created once per app instance rather than per render, so the cache survives re-renders.
  const [queryClient] = useState(createQueryClient);

  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <SessionBootstrap>{children}</SessionBootstrap>
      </ToastProvider>
    </QueryClientProvider>
  );
};
