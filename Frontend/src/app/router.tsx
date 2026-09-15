import { lazy, Suspense, type ReactNode } from 'react';
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router';
import { AppProviders } from './providers';
import { PublicLayout } from './PublicLayout';
import { AdminLayout } from './AdminLayout';
import { RequireAnonymous, RequireAuth } from './guards';
import { RouteErrorBoundary } from './RouteErrorBoundary';
import { StorefrontPage } from '../features/catalog/pages/StorefrontPage';
import { Skeleton } from '../shared/components/ui/States';
import { Permissions } from '../shared/lib/permissions';

/**
 * Route-level code splitting (spec §14 Phase 11).
 *
 * The storefront is eager because it is the landing route - lazily loading the first thing
 * every visitor sees would add a round trip to the critical path for no benefit. Everything
 * else is split, so a shopper never downloads the admin app and Recharts is only fetched by
 * the two screens that draw charts.
 */
const lazyPage = <T extends Record<string, React.ComponentType>>(
  loader: () => Promise<T>,
  name: keyof T,
) => lazy(async () => ({ default: (await loader())[name] }));

const ProductDetailPage = lazyPage(() => import('../features/catalog/pages/ProductDetailPage'), 'ProductDetailPage');
const NewArrivalsPage = lazyPage(() => import('../features/catalog/pages/NewArrivalsPage'), 'NewArrivalsPage');
const CartPage = lazyPage(() => import('../features/cart/pages/CartPage'), 'CartPage');
const CheckoutPage = lazyPage(() => import('../features/checkout/pages/CheckoutPage'), 'CheckoutPage');
const OrderConfirmationPage = lazyPage(
  () => import('../features/checkout/pages/OrderConfirmationPage'),
  'OrderConfirmationPage',
);
const LoginPage = lazyPage(() => import('../features/auth/pages/LoginPage'), 'LoginPage');
const RegisterPage = lazyPage(() => import('../features/auth/pages/RegisterPage'), 'RegisterPage');

const AdminDashboardPage = lazyPage(() => import('../features/admin/pages/AdminDashboardPage'), 'AdminDashboardPage');
const AdminUsersPage = lazyPage(() => import('../features/admin/pages/AdminUsersPage'), 'AdminUsersPage');
const AdminProductsPage = lazyPage(() => import('../features/admin/pages/AdminCatalogPages'), 'AdminProductsPage');
const AdminCategoriesPage = lazyPage(() => import('../features/admin/pages/AdminCatalogPages'), 'AdminCategoriesPage');
const AdminOffersPage = lazyPage(() => import('../features/admin/pages/AdminCatalogPages'), 'AdminOffersPage');
const AdminOrdersPage = lazyPage(() => import('../features/admin/pages/AdminOrdersPage'), 'AdminOrdersPage');
const AdminOrderDetailPage = lazyPage(() => import('../features/admin/pages/AdminOrdersPage'), 'AdminOrderDetailPage');
const AdminAuditPage = lazyPage(() => import('../features/admin/pages/AdminAuditPage'), 'AdminAuditPage');
const AdminAccessPage = lazyPage(() => import('../features/admin/pages/AdminAccessPage'), 'AdminAccessPage');

const AccountDashboardPage = lazyPage(() => import('../features/account/pages/AccountPages'), 'AccountDashboardPage');
const AccountOrdersPage = lazyPage(() => import('../features/account/pages/AccountPages'), 'AccountOrdersPage');
const AccountOrderDetailPage = lazyPage(
  () => import('../features/account/pages/AccountPages'),
  'AccountOrderDetailPage',
);
const AccountProfilePage = lazyPage(() => import('../features/account/pages/AccountPages'), 'AccountProfilePage');

/** Skeleton while a route chunk loads, so a split never shows a blank frame. */
const Lazy = ({ children }: { children: ReactNode }) => (
  <Suspense fallback={<Skeleton className="h-96 w-full" />}>{children}</Suspense>
);

const ForbiddenPage = () => (
  <div className="mx-auto max-w-md py-16 text-center">
    <h1 className="text-2xl font-semibold text-content">You do not have access to that page</h1>
    <p className="mt-2 text-sm text-content-muted">
      If you believe this is a mistake, ask an administrator to review your role.
    </p>
  </div>
);

const router = createBrowserRouter([
  {
    // AppProviders sits inside the router because the session bootstrap uses useNavigate
    // to redirect on an expired session and that hook needs a router above it.
    element: (
      <AppProviders>
        <PublicLayout />
      </AppProviders>
    ),
    errorElement: <RouteErrorBoundary />,
    children: [
      { index: true, element: <StorefrontPage /> },
      { path: 'new', element: <Lazy><NewArrivalsPage /></Lazy> },
      { path: 'products/:slug', element: <Lazy><ProductDetailPage /></Lazy> },
      { path: 'cart', element: <Lazy><CartPage /></Lazy> },
      { path: 'checkout', element: <Lazy><CheckoutPage /></Lazy> },
      { path: 'order-confirmation/:orderNumber', element: <Lazy><OrderConfirmationPage /></Lazy> },
      { path: '403', element: <ForbiddenPage /> },

      {
        element: <RequireAnonymous />,
        children: [
          { path: 'login', element: <Lazy><LoginPage /></Lazy> },
          { path: 'register', element: <Lazy><RegisterPage /></Lazy> },
        ],
      },
    ],
  },

  // --- admin -------------------------------------------------------------
  {
    element: (
      <AppProviders>
        <RequireAuth permissions={[Permissions.DashboardAdmin, Permissions.UsersRead]} />
      </AppProviders>
    ),
    errorElement: <RouteErrorBoundary />,
    children: [
      {
        element: <AdminLayout title="Administration" />,
        children: [
          { path: 'admin', element: <Navigate to="/admin/dashboard" replace /> },
          { path: 'admin/dashboard', element: <Lazy><AdminDashboardPage /></Lazy> },
          { path: 'admin/users', element: <Lazy><AdminUsersPage /></Lazy> },
          { path: 'admin/categories', element: <Lazy><AdminCategoriesPage /></Lazy> },
          { path: 'admin/products', element: <Lazy><AdminProductsPage /></Lazy> },
          { path: 'admin/offers', element: <Lazy><AdminOffersPage /></Lazy> },
          { path: 'admin/orders', element: <Lazy><AdminOrdersPage /></Lazy> },
          { path: 'admin/orders/:id', element: <Lazy><AdminOrderDetailPage /></Lazy> },
          { path: 'admin/audit-trails', element: <Lazy><AdminAuditPage /></Lazy> },
          { path: 'admin/access', element: <Lazy><AdminAccessPage /></Lazy> },
        ],
      },
    ],
  },

  // --- customer account ---------------------------------------------------
  {
    element: (
      <AppProviders>
        <RequireAuth />
      </AppProviders>
    ),
    errorElement: <RouteErrorBoundary />,
    children: [
      {
        element: <AdminLayout title="Your account" />,
        children: [
          { path: 'account', element: <Navigate to="/account/dashboard" replace /> },
          { path: 'account/dashboard', element: <Lazy><AccountDashboardPage /></Lazy> },
          { path: 'account/orders', element: <Lazy><AccountOrdersPage /></Lazy> },
          { path: 'account/orders/:id', element: <Lazy><AccountOrderDetailPage /></Lazy> },
          { path: 'account/profile', element: <Lazy><AccountProfilePage /></Lazy> },
        ],
      },
    ],
  },

  { path: '*', element: <Navigate to="/" replace /> },
]);

export const AppRouter = () => <RouterProvider router={router} />;
