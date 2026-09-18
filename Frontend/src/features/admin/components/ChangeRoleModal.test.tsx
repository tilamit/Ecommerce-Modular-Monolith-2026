import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ChangeRoleModal } from './ChangeRoleModal';
import { fetchRoles, fetchUsers, setUserRoles, type UserListItem } from '../api';
import { ToastProvider } from '../../../shared/components/ui/Toast';

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>();

  return { ...actual, fetchRoles: vi.fn(), fetchUsers: vi.fn(), setUserRoles: vi.fn() };
});

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = vi.fn(function showModal(this: HTMLDialogElement) {
    this.open = true;
  });

  HTMLDialogElement.prototype.close = vi.fn(function close(this: HTMLDialogElement) {
    this.open = false;
  });
});

const user = (id: string, fullName: string, email: string, roles: string[]): UserListItem => ({
  id,
  email,
  fullName,
  isActive: true,
  createdUtc: '2026-08-23T00:00:00Z',
  roles,
});

const grace = user('u-grace', 'Grace Hopper', 'grace@example.com', ['Customer']);
const alan = user('u-alan', 'Alan Turing', 'alan@example.com', ['Customer']);

const paged = <T,>(items: T[]) => ({
  items,
  page: 1,
  pageSize: 100,
  totalCount: items.length,
  totalPages: 1,
  hasNext: false,
  hasPrevious: false,
});

const role = (id: string, name: string) => ({
  id,
  name,
  isSystemRole: true,
  isActive: true,
  userCount: 1,
  permissionCount: 1,
});

const renderModal = (initial: UserListItem | null) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onSaved = vi.fn();

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <ToastProvider>{children}</ToastProvider>
    </QueryClientProvider>
  );

  render(<ChangeRoleModal open user={initial} onClose={vi.fn()} onSaved={onSaved} />, { wrapper });

  return { onSaved };
};

/** The suggestions live in a `datalist`, which jsdom renders but does not open. */
const suggestions = () =>
  [...document.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));

const userField = () => screen.getByLabelText(/^user/i);

describe('ChangeRoleModal', () => {
  beforeEach(() => {
    vi.mocked(fetchUsers).mockResolvedValue(paged([alan, grace]));
    vi.mocked(fetchRoles).mockResolvedValue(paged([role('r-admin', 'Admin'), role('r-customer', 'Customer')]));
    vi.mocked(setUserRoles).mockReset().mockResolvedValue(undefined);
  });

  it('suggests users by full name and email and preselects the role the user holds', async () => {
    renderModal(grace);

    // The preselected user is in the list before the query resolves, so waiting on "any
    // suggestion" would pass too early and assert against a list of one.
    await waitFor(() => expect(suggestions()).toContain('Alan Turing (alan@example.com)'));

    expect(suggestions()).toContain('Grace Hopper (grace@example.com)');
    expect(suggestions()).toContain('Alan Turing (alan@example.com)');
    // The one field now carries the choice, rather than a select below a search box.
    expect(userField()).toHaveValue('Grace Hopper (grace@example.com)');

    await waitFor(() => expect(screen.getByRole('combobox', { name: /^role/i })).toHaveValue('r-customer'));
    expect(screen.getByRole('button', { name: 'Save role' })).toBeDisabled();
  });

  it('saves the chosen role for the user picked from the suggestions', async () => {
    const { onSaved } = renderModal(null);
    const actor = userEvent.setup();

    await waitFor(() => expect(suggestions()).toContain('Alan Turing (alan@example.com)'));

    // Choosing a suggestion sets the field to that option's value in one go, which is what
    // pasting reproduces. jsdom cannot open the browser's own suggestion list.
    await actor.click(userField());
    await actor.paste('Alan Turing (alan@example.com)');

    await waitFor(() => expect(screen.getByRole('combobox', { name: /^role/i })).toHaveValue('r-customer'));

    await actor.selectOptions(screen.getByRole('combobox', { name: /^role/i }), 'r-admin');
    await actor.click(screen.getByRole('button', { name: 'Save role' }));

    await waitFor(() => expect(setUserRoles).toHaveBeenCalledWith('u-alan', ['r-admin']));
    expect(onSaved).toHaveBeenCalled();
  });

  it('says so when nothing matches what was typed', async () => {
    vi.mocked(fetchUsers).mockResolvedValue(paged<UserListItem>([]));
    renderModal(null);
    const actor = userEvent.setup();

    await actor.click(userField());
    await actor.paste('nobody@example.com');

    expect(await screen.findByText('No user matches that name or email.')).toBeInTheDocument();
  });
});
