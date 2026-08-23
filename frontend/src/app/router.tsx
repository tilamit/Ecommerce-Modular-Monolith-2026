import { createBrowserRouter, Navigate, RouterProvider } from 'react-router';
import { AppProviders } from './providers';
import { PublicLayout } from './PublicLayout';
import { StorefrontPage } from '../features/catalog/pages/StorefrontPage';
import { NewArrivalsPage } from '../features/catalog/pages/NewArrivalsPage';
import { ProductDetailPage } from '../features/catalog/pages/ProductDetailPage';
import { CartPage } from '../features/cart/pages/CartPage';
import { CheckoutPage } from '../features/checkout/pages/CheckoutPage';
import { OrderConfirmationPage } from '../features/checkout/pages/OrderConfirmationPage';
import { LoginPage } from '../features/auth/pages/LoginPage';
import { RegisterPage } from '../features/auth/pages/RegisterPage';
import { RequireAnonymous } from './guards';
import { RouteErrorBoundary } from './RouteErrorBoundary';

/**
 * The router (spec §11.2).
 *
 * `AppProviders` is mounted *inside* the router rather than around it, because the session
 * bootstrap needs `useNavigate` to redirect on an expired session - a hook that only works
 * beneath a router.
 */
const router = createBrowserRouter([
  {
    element: (
      <AppProviders>
        <PublicLayout />
      </AppProviders>
    ),
    errorElement: <RouteErrorBoundary />,
    children: [
      { index: true, element: <StorefrontPage /> },
      { path: 'new', element: <NewArrivalsPage /> },
      { path: 'products/:slug', element: <ProductDetailPage /> },
      { path: 'cart', element: <CartPage /> },
      { path: 'checkout', element: <CheckoutPage /> },
      { path: 'order-confirmation/:orderNumber', element: <OrderConfirmationPage /> },

      // Signed-in users are bounced away from login and register (spec §11.2).
      {
        element: <RequireAnonymous />,
        children: [
          { path: 'login', element: <LoginPage /> },
          { path: 'register', element: <RegisterPage /> },
        ],
      },

      { path: '403', element: <ForbiddenPage /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]);

function ForbiddenPage() {
  return (
    <div className="mx-auto max-w-md py-16 text-center">
      <h1 className="text-2xl font-semibold text-content">You do not have access to that page</h1>
      <p className="mt-2 text-sm text-content-muted">
        If you believe this is a mistake, ask an administrator to review your role.
      </p>
    </div>
  );
}

export const AppRouter = () => <RouterProvider router={router} />;
