import { Link, NavLink, Outlet } from 'react-router';
import { Moon, ShoppingCart, Sun, MonitorSmartphone, LogOut, UserRound } from 'lucide-react';
import { useAuthStore } from '../shared/api/authStore';
import { useCartStore } from '../features/cart/cartStore';
import { useTheme } from '../shared/hooks/useTheme';
import { logout } from '../features/auth/api';
import { Button } from '../shared/components/ui/Button';
import { SearchBox } from '../features/catalog/components/SearchBox';
import { cn } from '../shared/lib/cn';

const ThemeToggle = () => {
  const { theme, cycle } = useTheme();

  const Icon = theme === 'light' ? Sun : theme === 'dark' ? Moon : MonitorSmartphone;

  return (
    <button
      type="button"
      onClick={cycle}
      // The label names the current state, so the control is meaningful without the icon.
      aria-label={`Theme: ${theme}. Activate to change.`}
      className="rounded-lg p-2 text-content-muted transition-colors hover:bg-surface-sunken hover:text-content"
    >
      <Icon className="size-5" aria-hidden="true" />
    </button>
  );
};

const CartBadge = () => {
  const count = useCartStore((state) => state.cart?.items.reduce((t, i) => t + i.quantity, 0) ?? 0);

  return (
    <Link
      to="/cart"
      className="relative rounded-lg p-2 text-content-muted transition-colors hover:bg-surface-sunken hover:text-content"
      aria-label={count === 0 ? 'Cart, empty' : `Cart, ${count} item${count === 1 ? '' : 's'}`}
    >
      <ShoppingCart className="size-5" aria-hidden="true" />

      {count > 0 && (
        <span
          // aria-hidden: the count is already in the link's accessible name above, so
          // announcing it twice would be noise.
          aria-hidden="true"
          className="absolute -right-0.5 -top-0.5 flex size-4.5 items-center justify-center rounded-full bg-brand-600 text-[10px] font-semibold text-white"
        >
          {count > 99 ? '99+' : count}
        </span>
      )}
    </Link>
  );
};

const ExpiredCartNotice = () => {
  const showExpiredNotice = useCartStore((state) => state.showExpiredNotice);
  const dismiss = useCartStore((state) => state.dismissExpiredNotice);

  if (!showExpiredNotice) {
    return null;
  }

  // Spec §8.4: "clear the cart and show a dismissible 'your cart expired' notice."
  return (
    <div role="status" className="border-b border-warning/40 bg-warning/10 px-4 py-2">
      <div className="mx-auto flex max-w-7xl items-center justify-between gap-4">
        <p className="text-sm text-content">Your cart expired after an hour of inactivity and was cleared.</p>
        <Button variant="ghost" size="sm" onClick={dismiss}>
          Dismiss
        </Button>
      </div>
    </div>
  );
};

const navLinkClasses = ({ isActive }: { isActive: boolean }) =>
  cn(
    'rounded-lg px-3 py-2 text-sm font-medium transition-colors',
    isActive ? 'bg-surface-sunken text-content' : 'text-content-muted hover:text-content',
  );

export const PublicLayout = () => {
  const user = useAuthStore((state) => state.user);

  return (
    <div className="flex min-h-dvh flex-col bg-surface">
      {/* Skip link: the first tab stop, so keyboard users can bypass the nav (spec §11.4). */}
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-brand-600 focus:px-4 focus:py-2 focus:text-white"
      >
        Skip to content
      </a>

      <ExpiredCartNotice />

      <header className="sticky top-0 z-40 border-b border-border-subtle bg-surface/90 backdrop-blur">
        <div className="mx-auto flex max-w-7xl items-center gap-3 px-4 py-3">
          <Link to="/" className="text-lg font-semibold tracking-tight text-content">
            Shop<span className="text-brand-600">Hub</span>
          </Link>

          <nav aria-label="Main" className="hidden items-center gap-1 md:flex">
            <NavLink to="/" end className={navLinkClasses}>
              Shop
            </NavLink>
            <NavLink to="/new" className={navLinkClasses}>
              New arrivals
            </NavLink>
          </nav>

          <div className="ml-auto flex flex-1 items-center justify-end gap-1 sm:max-w-md">
            <SearchBox />
            <ThemeToggle />
            <CartBadge />

            {user === null ? (
              <Link
                to="/login"
                className="rounded-lg px-3 py-2 text-sm font-medium text-content-muted transition-colors hover:text-content"
              >
                Sign in
              </Link>
            ) : (
              <div className="flex items-center gap-1">
                <Link
                  to="/account/orders"
                  className="rounded-lg p-2 text-content-muted transition-colors hover:bg-surface-sunken hover:text-content"
                  aria-label={`Account: ${user.fullName}`}
                >
                  <UserRound className="size-5" aria-hidden="true" />
                </Link>
                <button
                  type="button"
                  onClick={() => void logout()}
                  aria-label="Sign out"
                  className="rounded-lg p-2 text-content-muted transition-colors hover:bg-surface-sunken hover:text-content"
                >
                  <LogOut className="size-5" aria-hidden="true" />
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <main id="main" className="mx-auto w-full max-w-7xl flex-1 px-4 py-6">
        <Outlet />
      </main>

      <footer className="border-t border-border-subtle px-4 py-6">
        <p className="mx-auto max-w-7xl text-sm text-content-muted">
          ShopHub - a .NET 10 modular monolith reference implementation.
        </p>
      </footer>
    </div>
  );
};
