import { useEffect, useState, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { bootstrapSession, setSessionExpiredHandler, ApiError } from '../shared/api/httpClient';
import { useAuthStore } from '../shared/api/authStore';
import { useCartStore } from '../features/cart/cartStore';
import { fetchMe } from '../features/auth/api';
import { ToastProvider } from '../shared/components/ui/Toast';

const createQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: {
        // Spec §11.3: 30s for lists. Cart and orders override this to 0 at the call site.
        staleTime: 30_000,
        retry: (failureCount, error) => {
          // Never retry a client error: a 403 or a 404 will not become a 200, and retrying
          // an auth failure just burns through the rate limiter.
          if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
            return false;
          }

          return failureCount < 2;
        },
        refetchOnWindowFocus: false,
      },
    },
  });

/**
 * Runs once on boot.
 *
 * Two things have to happen before the app can render meaningfully: the localStorage cart
 * is hydrated (and swept for expiry), and one silent refresh attempts to restore a session
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
