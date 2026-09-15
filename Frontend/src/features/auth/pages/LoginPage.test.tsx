import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { LoginPage } from './LoginPage';
import { login, fetchMe } from '../api';
import { ApiError } from '../../../shared/api/httpClient';

vi.mock('../api', () => ({
  login: vi.fn(),
  fetchMe: vi.fn(),
}));

const renderLogin = () => {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <LoginPage /> },
      { path: '/account/orders', element: <p>Purchase history page</p> },
    ],
    { initialEntries: ['/login?returnUrl=%2Faccount%2Forders'] },
  );

  render(<RouterProvider router={router} />);

  return router;
};

const submit = async () => {
  const user = userEvent.setup();

  await user.type(screen.getByLabelText(/email/i), 'grace@example.com');
  await user.type(screen.getByLabelText(/password/i), 'correct-password');
  await user.click(screen.getByRole('button', { name: /sign in/i }));
};

describe('LoginPage', () => {
  beforeEach(() => {
    vi.mocked(login).mockReset();
    vi.mocked(fetchMe).mockReset();
  });

  it('does not report a failed sign-in when only /auth/me fails', async () => {
    // The shape of the reported bug: login succeeds, /me is rate limited.
    vi.mocked(login).mockResolvedValue({} as Awaited<ReturnType<typeof login>>);
    vi.mocked(fetchMe).mockRejectedValue(
      new ApiError(429, { status: 429, title: 'Too many requests', code: 'rate_limited' }),
    );

    const router = renderLogin();
    await submit();

    expect(await screen.findByText('Purchase history page')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/account/orders');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('still reports a sign-in the API refused', async () => {
    vi.mocked(login).mockRejectedValue(
      new ApiError(403, { status: 403, title: 'Invalid email or password.', code: 'invalid_credentials' }),
    );

    const router = renderLogin();
    await submit();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(fetchMe).not.toHaveBeenCalled();
    expect(router.state.location.pathname).toBe('/login');
  });
});
