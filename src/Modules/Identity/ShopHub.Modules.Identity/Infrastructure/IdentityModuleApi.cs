using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Identity.Contracts;
using ShopHub.Modules.Identity.Domain;
using ShopHub.Modules.Identity.Persistence;

namespace ShopHub.Modules.Identity.Infrastructure;

/// <summary>
/// Identity's implementation of its own Contracts interface (spec §4.3).
/// <para>
/// Every method projects straight to a Contracts DTO - no entity ever leaves this class, so
/// there is nothing for a caller to accidentally couple to.
/// </para>
/// </summary>
internal sealed class IdentityModuleApi(IdentityDbContext db) : IIdentityModuleApi
{
    public async Task<IReadOnlyList<UserSummaryDto>> GetUserSummariesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return [];
        }

        var ids = userIds.Distinct().ToArray();

        return await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummaryDto(u.Id, u.FirstName + " " + u.LastName, u.Email, u.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserCountsDto> GetUserCountsAsync(CancellationToken cancellationToken = default)
    {
        // Aggregated in SQL rather than by counting materialised rows (spec §10.1).
        var counts = await db.Users
            .GroupBy(u => u.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new UserCountsDto(
            counts.FirstOrDefault(c => c.IsActive)?.Count ?? 0,
            counts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0);
    }

    public async Task<IReadOnlyList<DailyCountDto>> GetRegistrationsPerDayAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        var from = fromInclusive.ToDateTime(TimeOnly.MinValue);
        var to = toInclusive.ToDateTime(TimeOnly.MaxValue);

        var rows = await db.Users
            .Where(u => u.CreatedUtc >= from && u.CreatedUtc <= to)
            .GroupBy(u => u.CreatedUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Select(r => new DailyCountDto(DateOnly.FromDateTime(r.Day), r.Count))
            .OrderBy(r => r.Day)];
    }

    public async Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = User.Normalize(email);

        return await db.Users
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
