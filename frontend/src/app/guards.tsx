import { Navigate, Outlet, useLocation } from 'react-router';
import { useAuthStore, hasAnyPermission } from '../shared/api/authStore';

/** Keeps signed-in users off /login and /register (spec §11.2). */
export const RequireAnonymous = () => {
  const isSignedIn = useAuthStore((state) => state.accessToken !== null);

  return isSignedIn ? <Navigate to="/" replace /> : <Outlet />;
};

/**
 * Route guard for authenticated areas.
 *
 * Client-side hiding is UX, not security (spec §11.2): every endpoint behind these routes
 * enforces its own permission server-side. This only spares the user a page that would
 * fail anyway.
 */
export const RequireAuth = ({ permissions }: { permissions?: readonly string[] }) => {
  const isSignedIn = useAuthStore((state) => state.accessToken !== null);
  const location = useLocation();

  if (!isSignedIn) {
    const returnUrl = `${location.pathname}${location.search}`;

    return <Navigate to={`/login?returnUrl=${encodeURIComponent(returnUrl)}`} replace />;
  }

  // A route the role cannot see redirects to /403 rather than rendering an empty shell.
  if (permissions !== undefined && !hasAnyPermission(permissions)) {
    return <Navigate to="/403" replace />;
  }

  return <Outlet />;
};
