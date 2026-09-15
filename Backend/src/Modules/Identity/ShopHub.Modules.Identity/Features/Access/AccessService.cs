using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Identity.Infrastructure;
using ShopHub.Modules.Identity.Persistence;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Identity.Features.Access;

/// <summary>A menu node in the full editable tree (spec §9.1 <c>GET /menus</c>).</summary>
internal sealed record MenuAdminNode(
    Guid Id,
    Guid? ParentId,
    string Title,
    string? Icon,
    string? Route,
    int DisplayOrder,
    bool IsActive,
    string? RequiredPermissionCode,
    IReadOnlyList<MenuAdminNode> Children);

internal sealed record RoleMenuGrant(Guid MenuItemId, bool IsVisible);

internal sealed record SetRoleMenusRequest(IReadOnlyList<RoleMenuGrant> Menus);

/// <summary>
/// Access management (spec §9.1): the full menu tree for editing and per-role menu grants.
/// This is the data behind "an admin grants access to dynamic menus".
/// </summary>
internal sealed class AccessService(IdentityDbContext db, IMenuService menus, IAuditWriter audit)
{
    internal async Task<IReadOnlyList<MenuAdminNode>> GetMenuTreeAsync(CancellationToken cancellationToken)
    {
        var flat = await db.MenuItems
            .Select(m => new
            {
                m.Id,
                m.ParentId,
                m.Title,
                m.Icon,
                m.Route,
                m.DisplayOrder,
                m.IsActive,
                RequiredPermissionCode = m.RequiredPermission != null ? m.RequiredPermission.Code : null,
            })
            .ToListAsync(cancellationToken);

        // Unlike the runtime menu, this returns inactive nodes too - the admin screen has
        // to be able to see and re-enable them.
        List<MenuAdminNode> Build(Guid? parentId) =>
            [.. flat
                .Where(m => m.ParentId == parentId)
                .OrderBy(m => m.DisplayOrder)
                .ThenBy(m => m.Title, StringComparer.Ordinal)
                .Select(m => new MenuAdminNode(
                    m.Id,
                    m.ParentId,
                    m.Title,
                    m.Icon,
                    m.Route,
                    m.DisplayOrder,
                    m.IsActive,
                    m.RequiredPermissionCode,
                    Build(m.Id)))];

        return Build(null);
    }

    internal async Task<IReadOnlyList<RoleMenuGrant>> GetRoleMenusAsync(Guid roleId, CancellationToken cancellationToken)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            throw NotFoundException.For("Role", roleId);
        }

        return await db.RoleMenuItems
            .Where(rm => rm.RoleId == roleId)
            .Select(rm => new RoleMenuGrant(rm.MenuItemId, rm.IsVisible))
            .ToListAsync(cancellationToken);
    }

    internal async Task SetRoleMenusAsync(
        Guid roleId,
        IReadOnlyList<RoleMenuGrant> grants,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles
            .AsTracking()
            .Include(r => r.MenuItems)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw NotFoundException.For("Role", roleId);

        var menuIds = grants.Select(g => g.MenuItemId).Distinct().ToArray();
        var found = await db.MenuItems.CountAsync(m => menuIds.Contains(m.Id), cancellationToken);

        if (found != menuIds.Length)
        {
            throw new NotFoundException("menu_item_not_found", "One or more of the supplied menu items do not exist.");
        }

        // Recorded as the titles of the visible menus, which is what the grant means to a reader.
        // A duplicate id in the request resolves to its first occurrence, as ReplaceMenuItems does.
        var before = await VisibleMenuTitlesAsync(
            [.. role.MenuItems.Where(m => m.IsVisible).Select(m => m.MenuItemId)],
            cancellationToken);

        var after = await VisibleMenuTitlesAsync(
            [.. grants.DistinctBy(g => g.MenuItemId).Where(g => g.IsVisible).Select(g => g.MenuItemId)],
            cancellationToken);

        role.ReplaceMenuItems(grants.Select(g => (g.MenuItemId, g.IsVisible)));
        await db.SaveChangesAsync(cancellationToken);

        // The sidebar is cached per role set; revoking a menu must show up on next sign-in.
        await menus.InvalidateAsync(cancellationToken);

        AccessChangeAudit.Write(audit, role, AccessChangeAudit.MenusField, before, after);
    }

    private async Task<IReadOnlyList<string>> VisibleMenuTitlesAsync(
        IReadOnlyCollection<Guid> menuItemIds,
        CancellationToken cancellationToken) =>
        await db.MenuItems
            .Where(m => menuItemIds.Contains(m.Id))
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Title)
            .Select(m => m.Title)
            .ToListAsync(cancellationToken);
}

/// <summary><c>/api/v1/menus</c> and the role-menu grant endpoints (spec §9.1).</summary>
internal static class AccessEndpoints
{
    internal static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("/api/v1/menus", GetMenuTreeAsync)
            .WithTags("Access")
            .RequireRateLimiting(RateLimitPolicies.Authenticated)
            .RequireAuthorization(Permissions.AccessManage);

        var roleMenus = endpoints
            .MapGroup("/api/v1/roles/{id:guid}/menus")
            .WithTags("Access")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        roleMenus.MapGet("/", GetRoleMenusAsync).RequireAuthorization(Permissions.AccessManage);

        roleMenus.MapPut("/", SetRoleMenusAsync)
            .RequireAuthorization(Permissions.AccessManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        return endpoints;
    }

    private static async Task<IResult> GetMenuTreeAsync(AccessService access, CancellationToken cancellationToken) =>
        Results.Ok(await access.GetMenuTreeAsync(cancellationToken));

    private static async Task<IResult> GetRoleMenusAsync(Guid id, AccessService access, CancellationToken cancellationToken) =>
        Results.Ok(await access.GetRoleMenusAsync(id, cancellationToken));

    private static async Task<IResult> SetRoleMenusAsync(
        Guid id,
        SetRoleMenusRequest request,
        AccessService access,
        CancellationToken cancellationToken)
    {
        await access.SetRoleMenusAsync(id, request.Menus, cancellationToken);

        return Results.NoContent();
    }
}
