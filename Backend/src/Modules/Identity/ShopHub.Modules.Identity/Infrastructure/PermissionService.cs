using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using ShopHub.Modules.Identity.Persistence;
using ShopHub.Shared.Infrastructure.Caching;
using ShopHub.Shared.Infrastructure.Security;

namespace ShopHub.Modules.Identity.Infrastructure;

internal interface IPermissionService : IUserPermissionResolver
{
    /// <summary>Drops the cached permission set for one user, after a role change.</summary>
    Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Drops every cached permission set, after a change to a role's permissions - which
    /// can affect any number of users at once.
    /// </summary>
    Task InvalidateAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Resolves a user's effective permissions through their roles (spec §7.5), cached for
/// 10 minutes and invalidated on role or permission change.
/// <para>
/// This runs on every authorized request, so it is deliberately a single projected query
/// with no entity materialisation.
/// </para>
/// </summary>
internal sealed class PermissionService(IdentityDbContext db, HybridCache cache) : IPermissionService
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(10),
    };

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // State is passed through rather than captured, so the factory stays static and
        // allocates no closure on this per-request path.
        var codes = await cache.GetOrCreateSharedAsync(
            CacheKeys.For("identity", "permissions:user", userId),
            (Db: db, UserId: userId),
            static async (state, ct) => await state.Db.UserRoles
                .Where(ur => ur.UserId == state.UserId && ur.Role.IsActive)
                .SelectMany(ur => ur.Role.Permissions)
                .Select(rp => rp.Permission.Code)
                .Distinct()
                .ToArrayAsync(ct),
            CacheOptions,
            tags: [CacheTags.Permissions]);

        return codes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKeys.For("identity", "permissions:user", userId), cancellationToken);

    public async Task InvalidateAllAsync(CancellationToken cancellationToken) =>
        await cache.RemoveByTagAsync(CacheTags.Permissions, cancellationToken);
}
