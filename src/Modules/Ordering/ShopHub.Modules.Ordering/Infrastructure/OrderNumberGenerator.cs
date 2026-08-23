using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Ordering.Persistence;

namespace ShopHub.Modules.Ordering.Infrastructure;

/// <summary>
/// Produces <c>ORD-yyyyMMdd-NNNNNN</c>, sequential within a day (spec §8.3).
/// </summary>
/// <remarks>
/// The counter is derived from the day's existing maximum rather than from a shared
/// sequence, which means two concurrent checkouts can compute the same number. That is why
/// <c>IX_Orders_OrderNumber</c> is unique and why the caller retries on collision: the
/// database is the arbiter, not this method. A dedicated SQL SEQUENCE would remove the
/// retry, and is the obvious upgrade if checkout volume ever makes collisions common.
/// </remarks>
internal sealed class OrderNumberGenerator(OrderingDbContext db) : IOrderNumberGenerator
{
    private const string Prefix = "ORD";

    public async Task<string> NextAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var datePart = nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var todayPrefix = $"{Prefix}-{datePart}-";

        // Take the max rather than a count: a cancelled or deleted row must not cause a
        // number to be reused.
        var lastToday = await db.Orders
            .Where(o => o.OrderNumber.StartsWith(todayPrefix))
            .OrderByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 1;

        if (lastToday is not null
            && int.TryParse(lastToday[todayPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var previous))
        {
            next = previous + 1;
        }

        return $"{todayPrefix}{next:D6}";
    }
}
