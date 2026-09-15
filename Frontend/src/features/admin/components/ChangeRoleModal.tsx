import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { fetchRoles, setUserRoles, userLabel, type UserListItem } from '../api';
import { UserPicker } from './UserPicker';
import { Button } from '../../../shared/components/ui/Button';
import { Select } from '../../../shared/components/ui/Field';
import { Modal } from '../../../shared/components/ui/Modal';
import { useToast } from '../../../shared/components/ui/Toast';
import { ApiError } from '../../../shared/api/httpClient';

interface ChangeRoleModalProps {
  open: boolean;
  /** The row the dialog was opened from, preselected. Null when opened from the toolbar. */
  user: UserListItem | null;
  onClose: () => void;
  onSaved: () => void;
}

/**
 * Changes the role a user holds.
 *
 * The user is chosen by full name and email and the role from every role that exists. Saving
 * replaces the user's roles with the chosen one and the server records the change in the
 * audit trail with the role names before and after. The new permissions apply to the user's
 * next request; their sidebar follows on their next sign-in or page reload.
 */
export const ChangeRoleModal = ({ open, user, onClose, onSaved }: ChangeRoleModalProps) => {
  const { show } = useToast();

  const [selectedUser, setSelectedUser] = useState<UserListItem | null>(user);
  const [roleId, setRoleId] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const rolesQuery = useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: ({ signal }) => fetchRoles(signal),
    enabled: open,
  });

  const roles = rolesQuery.data?.items ?? [];

  useEffect(() => {
    if (open) {
      setSelectedUser(user);
      setError(null);
    }
  }, [open, user]);

  // Preselect the role the chosen user holds now, once both the user and the roles are known.
  useEffect(() => {
    const current = roles.find((role) => selectedUser?.roles.includes(role.name) === true);
    setRoleId(current?.id ?? '');
    // `roles` is rebuilt on every render; its length changing is what signals the load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedUser?.id, roles.length]);

  const chosenRole = roles.find((role) => role.id === roleId);
  const isUnchanged =
    selectedUser !== null &&
    chosenRole !== undefined &&
    selectedUser.roles.length === 1 &&
    selectedUser.roles[0] === chosenRole.name;

  const submit = async () => {
    if (selectedUser === null || chosenRole === undefined) {
      return;
    }

    setIsSaving(true);
    setError(null);

    try {
      await setUserRoles(selectedUser.id, [chosenRole.id]);
      show({ tone: 'success', message: `${selectedUser.fullName} now has the ${chosenRole.name} role.` });
      onSaved();
      onClose();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The role could not be changed.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      title="Change role"
      description="The user's permissions change on their next request."
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button
            size="sm"
            onClick={() => void submit()}
            isLoading={isSaving}
            disabled={selectedUser === null || chosenRole === undefined || isUnchanged}
          >
            Save role
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error !== null && (
          <p role="alert" className="rounded-lg border border-danger/40 bg-danger/5 px-3 py-2 text-sm text-danger">
            {error}
          </p>
        )}

        <UserPicker
          label="User"
          value={selectedUser?.id ?? ''}
          selected={selectedUser}
          onChange={setSelectedUser}
        />

        <Select
          label="Role"
          required
          value={roleId}
          disabled={selectedUser === null || rolesQuery.isPending}
          onChange={(event) => setRoleId(event.target.value)}
          error={rolesQuery.isError ? 'Roles could not be loaded.' : undefined}
          hint={
            selectedUser === null
              ? 'Choose a user first.'
              : `${userLabel(selectedUser)} currently has: ${selectedUser.roles.join(', ') || 'no role'}.`
          }
        >
          <option value="">Choose a role</option>
          {roles.map((role) => (
            <option key={role.id} value={role.id}>
              {role.isActive ? role.name : `${role.name} (inactive)`}
            </option>
          ))}
        </Select>
      </div>
    </Modal>
  );
};
