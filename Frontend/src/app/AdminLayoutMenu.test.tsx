import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { AdminLayout } from './AdminLayout';
import { fetchMe } from '../features/auth';
import { useAuthStore } from '../shared/api/authStore';
import type { MeResponse, MenuNode, UserProfile } from '../shared/api/types';

vi.mock('../features/auth', () => ({
  fetchMe: vi.fn(),
  logout: vi.fn(),
}));

const grace: UserProfile = {
  id: 'u2',
  email: 'grace@example.com',
  firstName: 'Grace',
  lastName: 'Hopper',
  fullName: 'Grace Hopper',
  isActive: true,
  roles: ['Customer'],
  permissions: ['orders.read.own'],
};

const purchaseHistory: MenuNode = {
  id: 'm1',
  title: 'Purchase History',
  icon: 'history',
  route: '/account/orders',
  displayOrder: 120,
  children: [],
};

const renderLayout = () => {
  const router = createMemoryRouter(
    [{ path: '/account', element: <AdminLayout title="Your account" />, children: [{ path: 'orders', element: <p>orders</p> }] }],
    { initialEntries: ['/account/orders'] },
  );

  render(<RouterProvider router={router} />);
};

const signInWithoutMenu = () => {
  useAuthStore.getState().clear();
  useAuthStore.getState().setSession({
    accessToken: 'token',
    accessTokenExpiresUtc: new Date(Date.now() + 60_000).toISOString(),
    refreshTokenExpiresUtc: new Date(Date.now() + 3_600_000).toISOString(),
    user: grace,
  });
};

/**
 * The sidebar a signed-in customer saw when /auth/me was rate limited at sign-in: a session,
 * no menu and a message saying their role had no access.
 */
describe('AdminLayout menu loading', () => {
  beforeEach(() => {
    vi.mocked(fetchMe).mockReset();
    signInWithoutMenu();
  });

  it('loads a menu that sign-in did not, instead of saying the role has none', async () => {
    vi.mocked(fetchMe).mockImplementation(() => {
      useAuthStore.getState().setMenu([purchaseHistory]);

      return Promise.resolve({ profile: grace, menu: [purchaseHistory] } satisfies MeResponse);
    });

    renderLayout();

    expect((await screen.findAllByRole('link', { name: 'Purchase History' })).length).toBeGreaterThan(0);
    expect(screen.queryByText(/no menu items/i)).not.toBeInTheDocument();
  });

  it('says the menu failed to load and retries on request', async () => {
    vi.mocked(fetchMe).mockRejectedValueOnce(new Error('429'));

    renderLayout();

    expect(await screen.findByText('The menu could not be loaded.')).toBeInTheDocument();
    expect(screen.queryByText(/no menu items/i)).not.toBeInTheDocument();

    vi.mocked(fetchMe).mockImplementation(() => {
      useAuthStore.getState().setMenu([purchaseHistory]);

      return Promise.resolve({ profile: grace, menu: [purchaseHistory] });
    });

    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }));

    expect((await screen.findAllByRole('link', { name: 'Purchase History' })).length).toBeGreaterThan(0);
  });

  it('keeps "no menu items" for a role that genuinely has none', async () => {
    useAuthStore.getState().setMenu([]);

    renderLayout();

    expect(await screen.findByText(/no menu items are available/i)).toBeInTheDocument();
    expect(fetchMe).not.toHaveBeenCalled();
  });
});
