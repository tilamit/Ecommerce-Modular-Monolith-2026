import { useState } from 'react';
import { useQuery, useQueryClient, useMutation, keepPreviousData } from '@tanstack/react-query';
import { UserCog } from 'lucide-react';
import { fetchUsers, setUserStatus, type UserListItem } from '../api';
import { ListPage } from '../components/ListPage';
import { ChangeRoleModal } from '../components/ChangeRoleModal';
import { hasPermission } from '../../../shared/api/authStore';
import { Permissions } from '../../../shared/lib/permissions';
import { useListParams } from '../../../shared/hooks/useListParams';
import { Button } from '../../../shared/components/ui/Button';
import { useToast } from '../../../shared/components/ui/Toast';
import { formatDate, formatRelative } from '../../../shared/lib/format';
import type { Column } from '../../../shared/components/ui/DataTable';

export const AdminUsersPage = () => {
  const { params, update, toQuery, searchParams } = useListParams({ sort: '-createdUtc' });
  const queryClient = useQueryClient();
  const { show } = useToast();

  const isActive = searchParams.get('isActive') ?? '';

  const canManage = hasPermission(Permissions.UsersManage);
  const [isRoleModalOpen, setIsRoleModalOpen] = useState(false);
  const [roleUser, setRoleUser] = useState<UserListItem | null>(null);

  const openRoleModal = (user: UserListItem | null) => {
    setRoleUser(user);
    setIsRoleModalOpen(true);
  };

  const query = useQuery({
    queryKey: ['admin', 'users', params, isActive],
    queryFn: ({ signal }) => fetchUsers(toQuery({ isActive: isActive || undefined }), signal),
    // Spec §11.3: the table must not flash empty between pages.
    placeholderData: keepPreviousData,
  });

  const statusMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setUserStatus(id, active),
    onSuccess: async (_, variables) => {
      // Invalidate precisely (spec §11.3), not the whole cache.
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
      show({ tone: 'success', message: variables.active ? 'User activated.' : 'User deactivated.' });
    },
    onError: () => show({ tone: 'error', message: 'Could not change the user status.' }),
  });

  const columns: Column<UserListItem>[] = [
    { key: 'email', header: 'Email', sortable: true, render: (user) => user.email },
    { key: 'fullName', header: 'Name', render: (user) => user.fullName },
    {
      key: 'roles',
      header: 'Roles',
      hideOnCard: true,
      render: (user) => <span className="text-content-muted">{user.roles.join(', ')}</span>,
    },
    {
      key: 'isActive',
      header: 'Status',
      sortable: true,
      render: (user) => (
        <span className={user.isActive ? 'text-success' : 'text-content-muted'}>
          {user.isActive ? 'Active' : 'Inactive'}
        </span>
      ),
    },
    {
      key: 'createdUtc',
      header: 'Registered',
      sortable: true,
      render: (user) => <time dateTime={user.createdUtc}>{formatDate(user.createdUtc)}</time>,
    },
    {
      key: 'lastLoginUtc',
      header: 'Last seen',
      sortable: true,
      hideOnCard: true,
      render: (user) =>
        user.lastLoginUtc === null || user.lastLoginUtc === undefined ? (
          <span className="text-content-muted">Never</span>
        ) : (
          <time dateTime={user.lastLoginUtc}>{formatRelative(user.lastLoginUtc)}</time>
        ),
    },
    {
      key: 'actions',
      header: 'Actions',
      align: 'right',
      render: (user) =>
        canManage ? (
          <div className="flex items-center justify-end gap-1">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => openRoleModal(user)}
              aria-label={`Change the role of ${user.fullName}`}
            >
              Change role
            </Button>
            <Button
              variant="secondary"
              size="sm"
              isLoading={statusMutation.isPending && statusMutation.variables?.id === user.id}
              onClick={() => statusMutation.mutate({ id: user.id, active: !user.isActive })}
              aria-label={`${user.isActive ? 'Deactivate' : 'Activate'} ${user.fullName}`}
            >
              {user.isActive ? 'Deactivate' : 'Activate'}
            </Button>
          </div>
        ) : null,
    },
  ];

  return (
    <>
    <ListPage
      actions={
        canManage && (
          <Button size="sm" onClick={() => openRoleModal(null)}>
            <UserCog className="size-4" aria-hidden="true" />
            Change role
          </Button>
        )
      }
      title="Users"
      columns={columns}
      rowKey={(user) => user.id}
      query={query}
      search={params.search}
      onSearchChange={(search) => update({ search })}
      searchPlaceholder="Search by name or email…"
      sort={params.sort}
      onSortChange={(sort) => update({ sort })}
      onPageChange={(page) => update({ page })}
      emptyTitle="No users match those filters"
      filters={
        <label className="flex items-center gap-2 text-sm text-content-muted">
          Status
          <select
            value={isActive}
            onChange={(event) => update({ isActive: event.target.value })}
            className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
          >
            <option value="">All</option>
            <option value="true">Active</option>
            <option value="false">Inactive</option>
          </select>
        </label>
      }
    />

    {canManage && (
      <ChangeRoleModal
        open={isRoleModalOpen}
        user={roleUser}
        onClose={() => setIsRoleModalOpen(false)}
        onSaved={() => void queryClient.invalidateQueries({ queryKey: ['admin', 'users'] })}
      />
    )}
    </>
  );
};
