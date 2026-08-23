namespace ShopHub.Modules.Ordering.Contracts;

/// <summary>
/// Ordering's public surface to other modules and to the dashboard composition
/// (spec §9.3, §10).
/// </summary>
public interface IOrderingModuleApi
{
    /// <summary>Totals for one customer's dashboard (spec §10.2).</summary>
    Task<CustomerOrderSummaryDto> GetCustomerSummaryAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Order and revenue aggregates over a window, for the admin dashboard (spec §10.1).
    /// <para>
    /// Returns only days that have data. The caller zero-fills the gaps, because a chart
    /// that silently omits quiet days lies about them.
    /// </para>
    /// </summary>
    Task<SalesAggregatesDto> GetSalesAggregatesAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default);

    /// <summary>One customer's daily order count and spend, for their dashboard chart (spec §10.2).</summary>
    Task<IReadOnlyList<DailySalesDto>> GetCustomerDailyAsync(
        Guid userId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default);

    /// <summary>The customer's most recent orders, bounded (spec §10.2).</summary>
    Task<IReadOnlyList<RecentOrderDto>> GetCustomerRecentOrdersAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed record RecentOrderDto(
    Guid Id,
    string OrderNumber,
    DateTime PlacedUtc,
    string Status,
    decimal GrandTotal,
    string CurrencyCode,
    int ItemCount);

public sealed record CustomerOrderSummaryDto(
    int TotalOrders,
    decimal TotalSpent,
    int OrdersLast30Days,
    decimal SpentLast30Days,
    int PendingOrders,
    string CurrencyCode);

/// <summary>Aggregates for the admin dashboard, all computed in SQL rather than in memory.</summary>
public sealed record SalesAggregatesDto(
    int OrderCount,
    decimal Revenue,
    IReadOnlyList<DailySalesDto> PerDay,
    IReadOnlyList<CategoryRevenueDto> TopProductsByRevenue);

public sealed record DailySalesDto(DateOnly Day, int OrderCount, decimal Revenue);

/// <summary>
/// Revenue grouped by product. Named by product id because Ordering cannot resolve a
/// category - that is Catalog's schema, and there is no join across it (spec §4.2).
/// The dashboard maps ids to names through Catalog's Contracts.
/// </summary>
public sealed record CategoryRevenueDto(Guid ProductId, string ProductName, decimal Revenue, int UnitsSold);
