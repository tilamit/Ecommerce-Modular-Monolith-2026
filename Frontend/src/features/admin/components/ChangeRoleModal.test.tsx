import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
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

describe('ChangeRoleModal', () => {
  beforeEach(() => {
    vi.mocked(fetchUsers).mockResolvedValue(paged([alan, grace]));
    vi.mocked(fetchRoles).mockResolvedValue(paged([role('r-admin', 'Admin'), role('r-customer', 'Customer')]));
    vi.mocked(setUserRoles).mockReset().mockResolvedValue(undefined);
  });

  it('names users by full name and email and preselects the role the user holds', async () => {
    renderModal(grace);

    await screen.findByRole('option', { name: 'Alan Turing (alan@example.com)' });
    const userSelect = screen.getByRole('combobox', { name: /^user/i });
    const options = within(userSelect).getAllByRole('option').map((option) => option.textContent);

    expect(options).toContain('Grace Hopper (grace@example.com)');
    expect(options).toContain('Alan Turing (alan@example.com)');
    expect(userSelect).toHaveValue('u-grace');

    await waitFor(() => expect(screen.getByRole('combobox', { name: /^role/i })).toHaveValue('r-customer'));
    expect(screen.getByRole('button', { name: 'Save role' })).toBeDisabled();
  });

  it('saves the chosen role for the chosen user', async () => {
    const { onSaved } = renderModal(null);
    const actor = userEvent.setup();

    await screen.findByRole('option', { name: 'Alan Turing (alan@example.com)' });
    await actor.selectOptions(screen.getByRole('combobox', { name: /^user/i }), 'u-alan');
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^role/i })).toHaveValue('r-customer'));

    await actor.selectOptions(screen.getByRole('combobox', { name: /^role/i }), 'r-admin');
    await actor.click(screen.getByRole('button', { name: 'Save role' }));

    await waitFor(() => expect(setUserRoles).toHaveBeenCalledWith('u-alan', ['r-admin']));
    expect(onSaved).toHaveBeenCalled();
  });
});
