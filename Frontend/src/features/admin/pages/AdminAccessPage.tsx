import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  fetchMenuTree,
  fetchPermissions,
  fetchRoleMenus,
  fetchRolePermissions,
  fetchRoles,
  saveRoleMenus,
  saveRolePermissions,
  type MenuAdminNode,
} from '../api';
import { Button } from '../../../shared/components/ui/Button';
import { ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { useToast } from '../../../shared/components/ui/Toast';

const flatten = (nodes: MenuAdminNode[], depth = 0): { node: MenuAdminNode; depth: number }[] =>
  nodes.flatMap((node) => [{ node, depth }, ...flatten(node.children, depth + 1)]);

/** Adds or removes an id, producing a new Set so React sees the change. */
const toggle = (set: Set<string>, id: string, apply: (next: Set<string>) => void) => {
  const next = new Set(set);

  if (next.has(id)) {
    next.delete(id);
  } else {
    next.add(id);
  }

  apply(next);
};

/**
 * Access management (spec §9.1, §14 Phase 6).
 *
 * Two grants per role, edited side by side:
 *  - **permissions**, which decide what the API will allow, and
 *  - **menus**, which decide what the sidebar shows.
 *
 * They are separate on purpose. Hiding a menu is a UX choice; revoking a permission is the
 * security boundary. Revoking only the menu leaves the endpoint reachable by URL - which is
 * why the acceptance criterion checks both: "revoking a menu from a role changes the sidebar
 * on next login **and** returns 403 from the corresponding endpoints."
 */
export const AdminAccessPage = () => {
  const queryClient = useQueryClient();
  const { show } = useToast();

  const [roleId, setRoleId] = useState<string | null>(null);
  const [permissionIds, setPermissionIds] = useState<Set<string>>(new Set());
  const [menuIds, setMenuIds] = useState<Set<string>>(new Set());

  const rolesQuery = useQuery({ queryKey: ['admin', 'roles'], queryFn: ({ signal }) => fetchRoles(signal) });
  const permissionsQuery = useQuery({
    queryKey: ['admin', 'permissions'],
    queryFn: ({ signal }) => fetchPermissions(signal),
  });
  const menuQuery = useQuery({ queryKey: ['admin', 'menus'], queryFn: ({ signal }) => fetchMenuTree(signal) });

  // Default to the first role once the list arrives.
  useEffect(() => {
    if (roleId === null && rolesQuery.data !== undefined && rolesQuery.data.items.length > 0) {
      setRoleId(rolesQuery.data.items[0].id);
    }
  }, [roleId, rolesQuery.data]);

  const rolePermissionsQuery = useQuery({
    queryKey: ['admin', 'roles', roleId, 'permissions'],
    queryFn: ({ signal }) => fetchRolePermissions(roleId!, signal),
    enabled: roleId !== null,
  });

  const roleMenusQuery = useQuery({
    queryKey: ['admin', 'roles', roleId, 'menus'],
    queryFn: ({ signal }) => fetchRoleMenus(roleId!, signal),
    enabled: roleId !== null,
  });

  // Server state seeds the local draft; edits are local until saved.
  useEffect(() => {
    if (rolePermissionsQuery.data !== undefined) {
      setPermissionIds(new Set(rolePermissionsQuery.data));
    }
  }, [rolePermissionsQuery.data]);

  useEffect(() => {
    if (roleMenusQuery.data !== undefined) {
      setMenuIds(new Set(roleMenusQuery.data.filter((m) => m.isVisible).map((m) => m.menuItemId)));
    }
  }, [roleMenusQuery.data]);

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (roleId === null) {
        return;
      }

      await saveRolePermissions(roleId, [...permissionIds]);
      await saveRoleMenus(
        roleId,
        flatten(menuQuery.data ?? []).map(({ node }) => ({
          menuItemId: node.id,
          isVisible: menuIds.has(node.id),
        })),
      );
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'roles'] });
      show({
        tone: 'success',
        message: 'Access updated. Affected users see the change on their next sign-in.',
      });
    },
    onError: () => show({ tone: 'error', message: 'Could not save the access changes.' }),
  });

  if (rolesQuery.isPending || permissionsQuery.isPending || menuQuery.isPending) {
    return <Skeleton className="h-96 w-full" />;
  }

  if (rolesQuery.isError || permissionsQuery.isError || menuQuery.isError) {
    return <ErrorState onRetry={() => void rolesQuery.refetch()} />;
  }

  const grouped = permissionsQuery.data.reduce<Record<string, typeof permissionsQuery.data>>((acc, permission) => {
    (acc[permission.group] ??= []).push(permission);
    return acc;
  }, {});

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-content">Access management</h2>

        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-sm text-content-muted">
            Role
            <select
              value={roleId ?? ''}
              onChange={(event) => setRoleId(event.target.value)}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            >
              {rolesQuery.data.items.map((role) => (
                <option key={role.id} value={role.id}>
                  {role.name} ({role.userCount} users)
                </option>
              ))}
            </select>
          </label>

          <Button isLoading={saveMutation.isPending} onClick={() => saveMutation.mutate()}>
            Save changes
          </Button>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded-card border border-border-subtle p-4">
          <h3 className="text-sm font-semibold text-content">Permissions</h3>
          <p className="mt-1 text-xs text-content-muted">
            What the API allows. Endpoints check these, so this is the real boundary.
          </p>

          <div className="mt-3 flex flex-col gap-4">
            {Object.entries(grouped).map(([group, permissions]) => (
              <fieldset key={group}>
                <legend className="mb-1 text-xs font-semibold uppercase tracking-wide text-content-muted">
                  {group}
                </legend>

                <div className="flex flex-col gap-1">
                  {permissions.map((permission) => (
                    <label key={permission.id} className="flex items-start gap-2 text-sm text-content">
                      <input
                        type="checkbox"
                        checked={permissionIds.has(permission.id)}
                        onChange={() => toggle(permissionIds, permission.id, setPermissionIds)}
                        className="mt-0.5 size-4 rounded border-border-subtle"
                      />
                      <span>
                        {permission.displayName}
                        <span className="ml-2 font-mono text-xs text-content-muted">{permission.code}</span>
                      </span>
                    </label>
                  ))}
                </div>
              </fieldset>
            ))}
          </div>
        </section>

        <section className="rounded-card border border-border-subtle p-4">
          <h3 className="text-sm font-semibold text-content">Menu visibility</h3>
          <p className="mt-1 text-xs text-content-muted">
            What the sidebar shows. Hiding a menu is presentation only - the permission above
            is what stops the request.
          </p>

          <div className="mt-3 flex flex-col gap-1">
            {flatten(menuQuery.data).map(({ node, depth }) => (
              <label
                key={node.id}
                className="flex items-center gap-2 text-sm text-content"
                style={{ paddingLeft: `${depth * 1}rem` }}
              >
                <input
                  type="checkbox"
                  checked={menuIds.has(node.id)}
                  onChange={() => toggle(menuIds, node.id, setMenuIds)}
                  className="size-4 rounded border-border-subtle"
                />
                <span>
                  {node.title}
                  {node.route !== null && node.route !== undefined && (
                    <span className="ml-2 font-mono text-xs text-content-muted">{node.route}</span>
                  )}
                </span>
              </label>
            ))}
          </div>
        </section>
      </div>
    </div>
  );
};
