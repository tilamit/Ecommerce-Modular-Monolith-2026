import { useEffect, useMemo, useState } from 'react';
import { Link, NavLink, Outlet } from 'react-router';
import {
  LayoutDashboard,
  Users,
  FolderTree,
  Package,
  Tag,
  Receipt,
  ScrollText,
  ShieldCheck,
  History,
  UserRound,
  Menu,
  X,
  LogOut,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { useAuthStore } from '../shared/api/authStore';
import { fetchMe, logout } from '../features/auth';
import { cn } from '../shared/lib/cn';
import type { MenuNode } from '../shared/api/types';

/**
 * Maps the seeded icon name to a component.
 *
 * The API stores an icon *name*, not a component, because the menu is data (spec A3/A4).
 * An unknown name falls back rather than crashing the sidebar - adding a menu item in the
 * database must never be able to break the app.
 */
const ICONS: Record<string, LucideIcon> = {
  'layout-dashboard': LayoutDashboard,
  users: Users,
  'folder-tree': FolderTree,
  package: Package,
  tag: Tag,
  receipt: Receipt,
  'scroll-text': ScrollText,
  'shield-check': ShieldCheck,
  history: History,
  user: UserRound,
};

const iconFor = (name?: string | null): LucideIcon => ICONS[name ?? ''] ?? LayoutDashboard;

const linkClasses = ({ isActive }: { isActive: boolean }) =>
  cn(
    'flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
    isActive ? 'bg-brand-600 text-white' : 'text-content-muted hover:bg-surface-sunken hover:text-content',
  );

/**
 * Sections the sidebar groups its links under, matched on the route each menu item carries.
 *
 * The sidebar shows everything the role has been granted rather than only the area being
 * viewed. An administrator granted every menu was still shown eight of eleven, because the
 * account-area items were filtered out by whichever layout was mounted and no link led to
 * them. Grouping keeps the two areas legible without hiding either.
 */
const SECTIONS = [
  { prefix: '/admin', label: 'Administration' },
  { prefix: '/account', label: 'Your account' },
] as const;

export interface MenuSection {
  label: string;
  items: MenuNode[];
}

/**
 * Splits the granted menu into sections, in the order `SECTIONS` declares.
 *
 * Anything whose route matches no section is collected at the end rather than dropped: the
 * menu is data, so a route added later must still appear without a frontend change.
 */
export const toSections = (menu: MenuNode[]): MenuSection[] => {
  const matched = new Set<string>();

  const sections = SECTIONS.map(({ prefix, label }) => {
    const items = menu.filter((node) => node.route?.startsWith(prefix) === true);

    for (const item of items) {
      matched.add(item.id);
    }

    return { label, items };
  }).filter((section) => section.items.length > 0);

  const rest = menu.filter((node) => !matched.has(node.id));

  return rest.length > 0 ? [...sections, { label: 'More', items: rest }] : sections;
};

const SidebarLinks = ({ menu, onNavigate }: { menu: MenuNode[]; onNavigate?: () => void }) => (
  <ul className="flex flex-col gap-1">
    {menu
      .filter((node) => node.route !== null && node.route !== undefined)
      .map((node) => {
        const Icon = iconFor(node.icon);

        return (
          <li key={node.id}>
            <NavLink to={node.route!} className={linkClasses} onClick={onNavigate}>
              <Icon className="size-4 shrink-0" aria-hidden="true" />
              {node.title}
            </NavLink>

            {node.children.length > 0 && (
              <div className="ml-4 mt-1">
                <SidebarLinks menu={node.children} onNavigate={onNavigate} />
              </div>
            )}
          </li>
        );
      })}
  </ul>
);

/**
 * A heading is only worth its space when there is more than one section. A customer sees
 * their three account links and nothing to disambiguate them from.
 */
const SidebarSections = ({
  sections,
  onNavigate,
}: {
  sections: MenuSection[];
  onNavigate?: () => void;
}) => (
  <div className="flex flex-col gap-4">
    {sections.map((section) => (
      <div key={section.label}>
        {sections.length > 1 && (
          <p className="mb-1 px-3 text-xs font-semibold uppercase tracking-wide text-content-muted">
            {section.label}
          </p>
        )}

        <SidebarLinks menu={section.items} onNavigate={onNavigate} />
      </div>
    ))}
  </div>
);

/**
 * The sidebar body for each state the menu can be in.
 *
 * "No menu items" is kept for a role that genuinely has none. Shown while the menu was still
 * missing, it told a customer their role had no access when the request had simply failed.
 */
const SidebarBody = ({
  sections,
  isMenuLoaded,
  menuFailed,
  onRetry,
  onNavigate,
}: {
  sections: MenuSection[];
  isMenuLoaded: boolean;
  menuFailed: boolean;
  onRetry: () => void;
  onNavigate?: () => void;
}) => {
  if (!isMenuLoaded) {
    return menuFailed ? (
      <div className="flex flex-col items-start gap-2 px-3" role="alert">
        <p className="text-sm text-content-muted">The menu could not be loaded.</p>
        <button type="button" onClick={onRetry} className="text-sm font-medium text-brand-600 hover:underline">
          Try again
        </button>
      </div>
    ) : (
      <p className="px-3 text-sm text-content-muted" role="status">
        Loading menu…
      </p>
    );
  }

  if (sections.length === 0) {
    return <p className="px-3 text-sm text-content-muted">No menu items are available for your role.</p>;
  }

  return <SidebarSections sections={sections} onNavigate={onNavigate} />;
};

interface AdminLayoutProps {
  /**
   * Names the area for the heading and the navigation landmark. It no longer filters the
   * sidebar: the menu shows everything the role holds, whichever area is mounted.
   */
  title: string;
}

/**
 * Shell for the authenticated areas.
 *
 * The sidebar is rendered from the menu tree returned by `/auth/me`, never from a
 * hard-coded array (spec §11.2, requirement A4). Revoking a menu from a role in the
 * database changes this sidebar on the user's next sign-in, with no deploy - and the
 * routes behind it still enforce their own permissions server-side, because client-side
 * hiding is UX, not security.
 */
export const AdminLayout = ({ title }: AdminLayoutProps) => {
  const [isOpen, setIsOpen] = useState(false);
  const user = useAuthStore((state) => state.user);
  const menu = useAuthStore((state) => state.menu);
  const isMenuLoaded = useAuthStore((state) => state.isMenuLoaded);
  const [menuFailed, setMenuFailed] = useState(false);

  const sections = useMemo(() => toSections(menu), [menu]);

  const loadMenu = () => {
    setMenuFailed(false);
    fetchMe().catch(() => setMenuFailed(true));
  };

  // Sign-in and the boot refresh both load the menu, but neither fails on it - a signed-in
  // user is not turned away because /me did not answer. So the layout is what notices a
  // missing menu and asks again, once per mount; after that the retry is the user's call.
  useEffect(() => {
    if (user !== null && !isMenuLoaded) {
      fetchMe().catch(() => setMenuFailed(true));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per mount, not per store change
  }, []);

  const sidebarBody = (onNavigate?: () => void) => (
    <SidebarBody
      sections={sections}
      isMenuLoaded={isMenuLoaded}
      menuFailed={menuFailed}
      onRetry={loadMenu}
      onNavigate={onNavigate}
    />
  );

  return (
    <div className="flex min-h-dvh bg-surface">
      <a
        href="#admin-main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-brand-600 focus:px-4 focus:py-2 focus:text-white"
      >
        Skip to content
      </a>

      {/* Desktop sidebar */}
      <aside className="hidden w-60 shrink-0 border-r border-border-subtle p-4 lg:block">
        <Link to="/" className="mb-6 block text-lg font-semibold tracking-tight text-content">
          Shop<span className="text-brand-600">Hub</span>
        </Link>

        <nav aria-label={`${title} navigation`}>{sidebarBody()}</nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center gap-3 border-b border-border-subtle px-4 py-3">
          <button
            type="button"
            onClick={() => setIsOpen(true)}
            aria-label="Open navigation"
            aria-expanded={isOpen}
            className="rounded-lg p-2 text-content-muted hover:bg-surface-sunken hover:text-content lg:hidden"
          >
            <Menu className="size-5" aria-hidden="true" />
          </button>

          <h1 className="text-base font-semibold text-content">{title}</h1>

          <div className="ml-auto flex items-center gap-3">
            {user !== null && (
              <span className="hidden text-sm text-content-muted sm:inline">{user.fullName}</span>
            )}
            <Link to="/" className="text-sm text-content-muted hover:text-content hover:underline">
              Storefront
            </Link>
            <button
              type="button"
              onClick={() => void logout()}
              aria-label="Sign out"
              className="rounded-lg p-2 text-content-muted hover:bg-surface-sunken hover:text-content"
            >
              <LogOut className="size-5" aria-hidden="true" />
            </button>
          </div>
        </header>

        <main id="admin-main" className="min-w-0 flex-1 p-4 lg:p-6">
          <Outlet />
        </main>
      </div>

      {/* Mobile drawer */}
      {isOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <button
            type="button"
            aria-label="Close navigation"
            onClick={() => setIsOpen(false)}
            className="absolute inset-0 bg-black/40"
          />

          <div role="dialog" aria-modal="true" aria-label={`${title} navigation`} className="absolute inset-y-0 left-0 w-64 bg-surface p-4 shadow-xl">
            <div className="mb-6 flex items-center justify-between">
              <span className="text-lg font-semibold text-content">Menu</span>
              <button
                type="button"
                onClick={() => setIsOpen(false)}
                aria-label="Close navigation"
                className="rounded-lg p-2 text-content-muted hover:bg-surface-sunken"
              >
                <X className="size-5" aria-hidden="true" />
              </button>
            </div>

            <nav aria-label={`${title} navigation`}>{sidebarBody(() => setIsOpen(false))}</nav>
          </div>
        </div>
      )}
    </div>
  );
};
