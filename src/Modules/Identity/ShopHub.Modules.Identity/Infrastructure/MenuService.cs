using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using ShopHub.Modules.Identity.Features.Auth;
using ShopHub.Modules.Identity.Persistence;
using ShopHub.Shared.Infrastructure.Caching;

namespace ShopHub.Modules.Identity.Infrastructure;

internal interface IMenuService
{
    /// <summary>
    /// The menu tree a set of roles may see. The SPA renders its sidebar from this rather
    /// than from a hard-coded array (spec §11.2), which is what makes menu access a data
    /// change rather than a deploy.
    /// </summary>
    Task<IReadOnlyList<MenuNodeResponse>> GetMenuForRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken);

    Task InvalidateAsync(CancellationToken cancellationToken);
}

internal sealed class MenuService(IdentityDbContext db, HybridCache cache) : IMenuService
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(15),
        LocalCacheExpiration = TimeSpan.FromMinutes(15),
    };

    public async Task<IReadOnlyList<MenuNodeResponse>> GetMenuForRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return [];
        }

        // Key on the role set, not the user: every user in a role sees the same menu, so
        // caching per user would multiply identical entries.
        var key = CacheKeys.For("identity", "menus:roles", CacheKeys.HashFilter(roleIds.Order().ToArray()));

        var flat = await cache.GetOrCreateAsync(
            key,
            (Db: db, RoleIds: roleIds.ToArray()),
            static async (state, ct) => await state.Db.RoleMenuItems
                .Where(rm => state.RoleIds.Contains(rm.RoleId) && rm.IsVisible && rm.MenuItem.IsActive)
                .Select(rm => new FlatMenuNode(
                    rm.MenuItem.Id,
                    rm.MenuItem.ParentId,
                    rm.MenuItem.Title,
                    rm.MenuItem.Icon,
                    rm.MenuItem.Route,
                    rm.MenuItem.DisplayOrder))
                .Distinct()
                .ToArrayAsync(ct),
            CacheOptions,
            tags: [CacheTags.Menus],
            cancellationToken: cancellationToken);

        return BuildTree(flat, parentId: null);
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken) =>
        await cache.RemoveByTagAsync(CacheTags.Menus, cancellationToken);

    /// <summary>
    /// Assembles the flat rows into a tree. Done in memory because the set is small
    /// (a sidebar), and a recursive CTE would be harder to read for no measurable gain.
    /// <para>
    /// A child whose parent is not visible to the role is dropped with its parent - an
    /// orphaned leaf would render as a top-level item the admin never granted.
    /// </para>
    /// </summary>
    private static List<MenuNodeResponse> BuildTree(IReadOnlyCollection<FlatMenuNode> nodes, Guid? parentId) =>
        [.. nodes
            .Where(n => n.ParentId == parentId)
            .OrderBy(n => n.DisplayOrder)
            .ThenBy(n => n.Title, StringComparer.Ordinal)
            .Select(n => new MenuNodeResponse(
                n.Id,
                n.Title,
                n.Icon,
                n.Route,
                n.DisplayOrder,
                BuildTree(nodes, n.Id)))];

    /// <summary>Cache-friendly row shape: a record, not an entity (spec §6.4).</summary>
    private sealed record FlatMenuNode(Guid Id, Guid? ParentId, string Title, string? Icon, string? Route, int DisplayOrder);
}
