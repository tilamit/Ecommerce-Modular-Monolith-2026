using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Ordering.Contracts;
using ShopHub.Modules.Ordering.Domain;
using ShopHub.Modules.Ordering.Persistence;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Ordering.Infrastructure;

/// <summary>
/// Ordering's implementation of its own Contracts interface (spec §4.3). Every method
/// aggregates in SQL and projects to a DTO - no entity leaves this class.
/// </summary>
internal sealed class OrderingModuleApi(OrderingDbContext db, IClock clock) : IOrderingModuleApi
{
    /// <summary>Statuses that count as revenue. A cancelled order is not a sale.</summary>
    private static readonly OrderStatus[] RevenueStatuses =
    [
        OrderStatus.Pending,
        OrderStatus.Confirmed,
        OrderStatus.Processing,
        OrderStatus.Shipped,
        OrderStatus.Delivered,
    ];

    private const int TopProductCount = 5;

    public async Task<CustomerOrderSummaryDto> GetCustomerSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var since = clock.UtcNow.AddDays(-30);

        var totals = await db.Orders
            .Where(o => o.UserId == userId && RevenueStatuses.Contains(o.Status))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalOrders = g.Count(),
                TotalSpent = g.Sum(o => o.GrandTotal),
                OrdersLast30Days = g.Count(o => o.PlacedUtc >= since),
                SpentLast30Days = g.Where(o => o.PlacedUtc >= since).Sum(o => o.GrandTotal),
                PendingOrders = g.Count(o => o.Status == OrderStatus.Pending),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null
            ? new CustomerOrderSummaryDto(0, 0m, 0, 0m, 0, "USD")
            : new CustomerOrderSummaryDto(
                totals.TotalOrders,
                totals.TotalSpent,
                totals.OrdersLast30Days,
                totals.SpentLast30Days,
                totals.PendingOrders,
                "USD");
    }

    public async Task<IReadOnlyList<DailySalesDto>> GetCustomerDailyAsync(
        Guid userId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        var from = fromInclusive.ToDateTime(TimeOnly.MinValue);
        var to = toInclusive.ToDateTime(TimeOnly.MaxValue);

        var rows = await db.Orders
            .Where(o => o.UserId == userId
                && o.PlacedUtc >= from
                && o.PlacedUtc <= to
                && RevenueStatuses.Contains(o.Status))
            .GroupBy(o => o.PlacedUtc.Date)
            .Select(g => new { Day = g.Key, OrderCount = g.Count(), Revenue = g.Sum(o => o.GrandTotal) })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Select(r => new DailySalesDto(DateOnly.FromDateTime(r.Day), r.OrderCount, r.Revenue))
            .OrderBy(r => r.Day)];
    }

    public async Task<IReadOnlyList<RecentOrderDto>> GetCustomerRecentOrdersAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default) =>
        await db.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.PlacedUtc)
            .ThenBy(o => o.Id)
            .Take(Math.Clamp(take, 1, 50))
            .Select(o => new RecentOrderDto(
                o.Id,
                o.OrderNumber,
                o.PlacedUtc,
                o.Status.ToString(),
                o.GrandTotal,
                o.CurrencyCode,
                o.Items.Count))
            .ToListAsync(cancellationToken);

    public async Task<SalesAggregatesDto> GetSalesAggregatesAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        var from = fromInclusive.ToDateTime(TimeOnly.MinValue);
        var to = toInclusive.ToDateTime(TimeOnly.MaxValue);

        var inWindow = db.Orders.Where(o =>
            o.PlacedUtc >= from
            && o.PlacedUtc <= to
            && RevenueStatuses.Contains(o.Status));

        // GROUP BY CAST(PlacedUtc AS date) in SQL, never by pulling rows into memory
        // (spec §10.1).
        var perDay = await inWindow
            .GroupBy(o => o.PlacedUtc.Date)
            .Select(g => new { Day = g.Key, OrderCount = g.Count(), Revenue = g.Sum(o => o.GrandTotal) })
            .ToListAsync(cancellationToken);

        // Projected to an anonymous type first: EF cannot translate a GroupBy whose
        // Select constructs a record with aggregate arguments, so the DTO is built after
        // materialisation. The grouping and the SUMs still run in SQL.
        var topRows = await db.OrderItems
            .Where(i => db.Orders.Any(o => o.Id == i.OrderId
                && o.PlacedUtc >= from
                && o.PlacedUtc <= to
                && RevenueStatuses.Contains(o.Status)))
            .GroupBy(i => new { i.ProductId, i.ProductNameSnapshot })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductNameSnapshot,
                Revenue = g.Sum(i => i.LineTotal),
                UnitsSold = g.Sum(i => i.Quantity),
            })
            .OrderByDescending(r => r.Revenue)
            .Take(TopProductCount)
            .ToListAsync(cancellationToken);

        var topProducts = topRows
            .Select(r => new CategoryRevenueDto(r.ProductId, r.ProductNameSnapshot, r.Revenue, r.UnitsSold))
            .ToList();

        return new SalesAggregatesDto(
            perDay.Sum(d => d.OrderCount),
            perDay.Sum(d => d.Revenue),
            // Only days with data. The caller zero-fills, because a chart that omits quiet
            // days lies about them (spec §10.1).
            [.. perDay
                .Select(d => new DailySalesDto(DateOnly.FromDateTime(d.Day), d.OrderCount, d.Revenue))
                .OrderBy(d => d.Day)],
            topProducts);
    }
}
