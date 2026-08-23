namespace ShopHub.Api.Dashboards;

/// <summary>Admin dashboard payload (spec §10.1). One endpoint, one round trip.</summary>
internal sealed record AdminDashboardResponse(
    AdminCounts Counts,
    AdminCharts Charts,
    IReadOnlyList<RecentAuditEntry> RecentAudit,
    DateTime GeneratedUtc);

internal sealed record AdminCounts(
    int ActiveUsers,
    int InactiveUsers,
    int ActiveProducts,
    int InactiveProducts,
    int ActiveCategories,
    int OrdersLast30Days,
    decimal RevenueLast30Days,
    string CurrencyCode);

internal sealed record AdminCharts(
    IReadOnlyList<DailyPoint> UsersRegisteredPerDay,
    IReadOnlyList<DailyPoint> OrdersPerDay,
    IReadOnlyList<DailyMoneyPoint> RevenuePerDay,
    IReadOnlyList<TopSellerPoint> TopProductsByRevenue);

/// <summary>
/// One day of a series. Every day in the window is present, including zeros - a chart that
/// silently omits quiet days lies about them (spec §10.1).
/// </summary>
internal sealed record DailyPoint(DateOnly Day, int Value);

internal sealed record DailyMoneyPoint(DateOnly Day, decimal Value);

internal sealed record TopSellerPoint(Guid ProductId, string ProductName, decimal Revenue, int UnitsSold);

internal sealed record RecentAuditEntry(
    long Id,
    DateTime OccurredUtc,
    string Action,
    string Module,
    string EntityName,
    string? UserName,
    string? ScreenName);

/// <summary>Customer dashboard payload (spec §10.2). Scoped to the caller server-side.</summary>
internal sealed record CustomerDashboardResponse(
    CustomerCounts Counts,
    CustomerCharts Charts,
    IReadOnlyList<RecentOrderSummary> RecentOrders,
    DateTime GeneratedUtc);

internal sealed record CustomerCounts(
    int TotalOrders,
    decimal TotalSpent,
    int OrdersLast30Days,
    decimal SpentLast30Days,
    int PendingOrders,
    string CurrencyCode);

internal sealed record CustomerCharts(
    IReadOnlyList<DailyMoneyPoint> SpendPerDay,
    IReadOnlyList<DailyPoint> OrdersPerDay);

internal sealed record RecentOrderSummary(
    Guid Id,
    string OrderNumber,
    DateTime PlacedUtc,
    string Status,
    decimal GrandTotal,
    string CurrencyCode,
    int ItemCount);
