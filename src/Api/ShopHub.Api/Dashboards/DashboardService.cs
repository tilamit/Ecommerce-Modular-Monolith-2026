using Microsoft.Extensions.Caching.Hybrid;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Catalog.Contracts;
using ShopHub.Modules.Identity.Contracts;
using ShopHub.Modules.Ordering.Contracts;
using ShopHub.Shared.Infrastructure.Caching;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Api.Dashboards;

/// <summary>
/// Composes the dashboards from <b>Contracts calls only</b> (spec §10).
/// </summary>
/// <remarks>
/// <para>
/// This class lives in the API host rather than in a module precisely because it spans
/// them. It cannot query another module's tables even if it wanted to - every DbContext in
/// the solution is <c>internal</c> to its own module, so the only reachable surface is the
/// Contracts interfaces injected here.
/// </para>
/// <para>
/// That is the whole point of the read-composition pattern: a cross-module report is a
/// composition of module APIs, not a join. If Ordering moved to its own database tomorrow,
/// this file would not change.
/// </para>
/// </remarks>
internal sealed class DashboardService(
    IIdentityModuleApi identity,
    ICatalogModuleApi catalog,
    IOrderingModuleApi ordering,
    IAuditModuleApi audit,
    HybridCache cache,
    IClock clock)
{
    /// <summary>Spec §10.1: the whole payload is cached for 5 minutes.</summary>
    private static readonly HybridCacheEntryOptions AdminCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
    };

    private const int WindowDays = 30;

    private const int RecentAuditCount = 10;

    private const int RecentOrderCount = 5;

    internal async Task<AdminDashboardResponse> GetAdminDashboardAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKeys.For("dashboard", "admin", "v1"),
            this,
            static async (service, ct) => await service.BuildAdminDashboardAsync(ct),
            AdminCacheOptions,
            tags: [CacheTags.Dashboards],
            cancellationToken: cancellationToken);

    private async Task<AdminDashboardResponse> BuildAdminDashboardAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var from = today.AddDays(-(WindowDays - 1));

        // Sequential rather than concurrent: each call resolves a scoped DbContext, and a
        // DbContext is not thread-safe. Parallelising here would be a race, not a speed-up.
        var userCounts = await identity.GetUserCountsAsync(cancellationToken);
        var catalogCounts = await catalog.GetActiveCountsAsync(cancellationToken);
        var sales = await ordering.GetSalesAggregatesAsync(from, today, cancellationToken);
        var registrations = await identity.GetRegistrationsPerDayAsync(from, today, cancellationToken);
        var recentAudit = await audit.GetRecentAsync(RecentAuditCount, cancellationToken);

        var spine = DateSpine(from, today);

        return new AdminDashboardResponse(
            new AdminCounts(
                userCounts.Active,
                userCounts.Inactive,
                catalogCounts.ActiveProducts,
                catalogCounts.InactiveProducts,
                catalogCounts.ActiveCategories,
                sales.OrderCount,
                sales.Revenue,
                "USD"),
            new AdminCharts(
                ZeroFill(spine, registrations.ToDictionary(r => r.Day, r => r.Count)),
                ZeroFill(spine, sales.PerDay.ToDictionary(d => d.Day, d => d.OrderCount)),
                ZeroFillMoney(spine, sales.PerDay.ToDictionary(d => d.Day, d => d.Revenue)),
                [.. sales.TopProductsByRevenue.Select(p =>
                    new TopSellerPoint(p.ProductId, p.ProductName, p.Revenue, p.UnitsSold))]),
            [.. recentAudit.Select(a => new RecentAuditEntry(
                a.Id,
                a.OccurredUtc,
                a.Action,
                a.Module,
                a.EntityName,
                a.UserName,
                a.ScreenName))],
            clock.UtcNow);
    }

    /// <summary>
    /// Spec §10.2. Scoped to <paramref name="userId"/>, which comes from the token - the
    /// endpoint never accepts a user id.
    /// </summary>
    internal async Task<CustomerDashboardResponse> GetCustomerDashboardAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var from = today.AddDays(-(WindowDays - 1));

        var summary = await ordering.GetCustomerSummaryAsync(userId, cancellationToken);
        var recent = await ordering.GetCustomerRecentOrdersAsync(userId, RecentOrderCount, cancellationToken);
        var perDay = await ordering.GetCustomerDailyAsync(userId, from, today, cancellationToken);

        var spine = DateSpine(from, today);

        return new CustomerDashboardResponse(
            new CustomerCounts(
                summary.TotalOrders,
                summary.TotalSpent,
                summary.OrdersLast30Days,
                summary.SpentLast30Days,
                summary.PendingOrders,
                summary.CurrencyCode),
            new CustomerCharts(
                ZeroFillMoney(spine, perDay.ToDictionary(d => d.Day, d => d.Revenue)),
                ZeroFill(spine, perDay.ToDictionary(d => d.Day, d => d.OrderCount))),
            [.. recent.Select(o => new RecentOrderSummary(
                o.Id,
                o.OrderNumber,
                o.PlacedUtc,
                o.Status,
                o.GrandTotal,
                o.CurrencyCode,
                o.ItemCount))],
            clock.UtcNow);
    }

    /// <summary>
    /// Generates every day in the window in C# (spec §10.1), so the aggregate can be
    /// left-joined onto a complete spine.
    /// </summary>
    private static List<DateOnly> DateSpine(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }

    /// <summary>
    /// Fills gaps with zero. Without this a quiet day is simply absent from the series, and
    /// the chart draws a straight line across it - which reads as "steady" rather than
    /// "nothing happened" (spec §10.1).
    /// </summary>
    private static IReadOnlyList<DailyPoint> ZeroFill(IReadOnlyList<DateOnly> spine, Dictionary<DateOnly, int> values) =>
        [.. spine.Select(day => new DailyPoint(day, values.GetValueOrDefault(day)))];

    private static IReadOnlyList<DailyMoneyPoint> ZeroFillMoney(
        IReadOnlyList<DateOnly> spine,
        Dictionary<DateOnly, decimal> values) =>
        [.. spine.Select(day => new DailyMoneyPoint(day, values.GetValueOrDefault(day)))];
}
