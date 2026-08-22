using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Identity.Domain;
using ShopHub.Modules.Identity.Infrastructure;
using ShopHub.Modules.Identity.Persistence;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Identity.Features.Roles;

internal sealed record RoleListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive,
    int UserCount,
    int PermissionCount);

internal sealed record CreateRoleRequest(string Name, string? Description);

internal sealed record UpdateRoleRequest(string Name, string? Description, bool IsActive);

internal sealed record PermissionListItem(Guid Id, string Code, string DisplayName, string Group, string? Description);

internal sealed record SetRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);

/// <summary>Roles and their permissions (spec §9.1).</summary>
internal sealed class RoleService(IdentityDbContext db, IPermissionService permissions)
{
    /// <summary>
    /// Paged like every other list (spec §6.5) - "no endpoint anywhere returns an unbounded
    /// collection, including dropdowns". Roles are few today; that is not a guarantee.
    /// </summary>
    internal async Task<PagedResult<RoleListItem>> GetRolesAsync(PagedRequest paging, CancellationToken cancellationToken)
    {
        var request = paging.Normalized();
        var query = db.Roles.AsQueryable();

        if (request.Search is { } search)
        {
            query = query.Where(r => r.Name.StartsWith(search));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(r => new RoleListItem(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystemRole,
                r.IsActive,
                db.UserRoles.Count(ur => ur.RoleId == r.Id),
                r.Permissions.Count))
            .ToListAsync(cancellationToken);

        return new PagedResult<RoleListItem>(items, request.Page, request.PageSize, total);
    }

    internal async Task<RoleListItem> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Name.Trim().ToUpperInvariant();

        if (await db.Roles.AnyAsync(r => r.NormalizedName == normalized, cancellationToken))
        {
            throw new ConflictException("role_name_taken", $"A role named '{request.Name}' already exists.");
        }

        var role = Role.Create(request.Name, request.Description);

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        return await GetRoleAsync(role.Id, cancellationToken);
    }

    internal async Task<RoleListItem> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await TrackedRoleAsync(id, cancellationToken);
        var normalized = request.Name.Trim().ToUpperInvariant();

        if (!string.Equals(role.NormalizedName, normalized, StringComparison.Ordinal)
            && await db.Roles.AnyAsync(r => r.NormalizedName == normalized && r.Id != id, cancellationToken))
        {
            throw new ConflictException("role_name_taken", $"A role named '{request.Name}' already exists.");
        }

        // Rename and SetActive both refuse on a system role (Role.EnsureMutable).
        role.Rename(request.Name, request.Description);
        role.SetActive(request.IsActive);

        await db.SaveChangesAsync(cancellationToken);

        // A deactivated role stops contributing permissions to its users.
        await permissions.InvalidateAllAsync(cancellationToken);

        return await GetRoleAsync(id, cancellationToken);
    }

    internal async Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await TrackedRoleAsync(id, cancellationToken);

        role.EnsureDeletable();

        // Deleting a role out from under its users would leave them with no role at all,
        // which User.ReplaceRoles treats as invalid. Refuse instead.
        var assigned = await db.UserRoles.CountAsync(ur => ur.RoleId == id, cancellationToken);

        if (assigned > 0)
        {
            throw new ConflictException(
                "role_in_use",
                $"This role is assigned to {assigned} user(s). Reassign them before deleting it.");
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        await permissions.InvalidateAllAsync(cancellationToken);
    }

    /// <summary>The full permission catalogue, for the access-management screen.</summary>
    internal async Task<IReadOnlyList<PermissionListItem>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        await db.Permissions
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Code)
            .Select(p => new PermissionListItem(p.Id, p.Code, p.DisplayName, p.Group, p.Description))
            .ToListAsync(cancellationToken);

    internal async Task<IReadOnlyList<Guid>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        await EnsureRoleExistsAsync(roleId, cancellationToken);

        return await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync(cancellationToken);
    }

    internal async Task SetRolePermissionsAsync(
        Guid roleId,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles
            .AsTracking()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw NotFoundException.For("Role", roleId);

        var found = await db.Permissions.CountAsync(p => permissionIds.Contains(p.Id), cancellationToken);

        if (found != permissionIds.Distinct().Count())
        {
            throw new NotFoundException("permission_not_found", "One or more of the supplied permissions do not exist.");
        }

        role.ReplacePermissions(permissionIds);
        await db.SaveChangesAsync(cancellationToken);

        // This can change the effective permissions of every user in the role, so the
        // per-user cache is cleared wholesale rather than user by user.
        await permissions.InvalidateAllAsync(cancellationToken);
    }

    private async Task<RoleListItem> GetRoleAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Roles
            .Where(r => r.Id == id)
            .Select(r => new RoleListItem(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystemRole,
                r.IsActive,
                db.UserRoles.Count(ur => ur.RoleId == r.Id),
                r.Permissions.Count))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw NotFoundException.For("Role", id);

    private async Task<Role> TrackedRoleAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Roles.AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
        ?? throw NotFoundException.For("Role", id);

    private async Task EnsureRoleExistsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            throw NotFoundException.For("Role", roleId);
        }
    }
}
