using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Catalog.Contracts;
using ShopHub.Modules.Identity.Contracts;
using ShopHub.Modules.Ordering.Domain;
using ShopHub.Modules.Ordering.Persistence;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Ordering.Features.Orders;

/// <summary>Registered or guest (spec §8.4 requirement C5).</summary>
internal enum CustomerType
{
    Registered = 1,
    Guest = 2,
}

internal sealed record OrderListItem(
    Guid Id,
    string OrderNumber,
    string Status,
    decimal GrandTotal,
    string CurrencyCode,
    DateTime PlacedUtc,
    int ItemCount,
    string CustomerType,
    string CustomerName,
    string CustomerEmail,
    string PaymentMethod);

internal sealed record OrderDetail(
    Guid Id,
    string OrderNumber,
    string Status,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal ShippingTotal,
    decimal GrandTotal,
    string CurrencyCode,
    string? AppliedOfferCode,
    string PaymentMethod,
    string PaymentStatus,
    DateTime PlacedUtc,
    string? Notes,
    string CustomerType,
    string CustomerName,
    string CustomerEmail,
    AddressResponse ShippingAddress,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory);

internal sealed record AddressResponse(
    string FullName,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country,
    string? PhoneNumber);

internal sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    string Sku,
    decimal UnitPrice,
    int Quantity,
    decimal DiscountAmount,
    decimal LineTotal);

internal sealed record OrderStatusHistoryResponse(
    string? FromStatus,
    string ToStatus,
    DateTime ChangedUtc,
    Guid? ChangedByUserId,
    string? Reason);

/// <summary>Admin order filters (spec §9.3).</summary>
internal sealed record OrderFilter(
    string? Search,
    IReadOnlyList<OrderStatus>? Statuses,
    DateTime? DateFrom,
    DateTime? DateTo,
    decimal? MinTotal,
    decimal? MaxTotal,
    CustomerType? CustomerType,
    PaymentMethod? PaymentMethod);

internal sealed record ChangeOrderStatusRequest(OrderStatus Status, string? Reason);

internal sealed record CancelOrderRequest(string? Reason);

/// <summary>
/// Order reads and status transitions (spec §9.3).
/// <para>
/// Customer identity is resolved <em>after</em> the SQL query, never joined into it:
/// <c>Orders.UserId</c> points into another module's schema (spec §8.4). Guest details do
/// come from a join, because <c>GuestCheckoutProfiles</c> is Ordering's own table.
/// </para>
/// </summary>
internal sealed class OrderService(
    OrderingDbContext db,
    IIdentityModuleApi identity,
    ICatalogModuleApi catalog,
    IClock clock)
{
    internal async Task<PagedResult<OrderListItem>> GetOrdersAsync(
        PagedRequest paging,
        OrderFilter filter,
        Guid? restrictToUserId,
        CancellationToken cancellationToken)
    {
        var request = paging.Normalized();
        var query = Filtered(filter, restrictToUserId);

        var total = await query.LongCountAsync(cancellationToken);

        var rows = await Sort(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(o => new OrderRow(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.GrandTotal,
                o.CurrencyCode,
                o.PlacedUtc,
                o.Items.Count,
                o.UserId,
                o.PaymentMethod,
                // Guest details are in Ordering's own schema, so this join is legitimate.
                db.GuestCheckoutProfiles
                    .Where(g => g.Id == o.GuestCheckoutProfileId)
                    .Select(g => new GuestRef(g.FullName, g.Email))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var customers = await ResolveCustomersAsync(rows.Select(r => r.UserId), cancellationToken);

        var items = rows
            .Select(r => new OrderListItem(
                r.Id,
                r.OrderNumber,
                r.Status.ToString(),
                r.GrandTotal,
                r.CurrencyCode,
                r.PlacedUtc,
                r.ItemCount,
                (r.UserId is not null ? CustomerType.Registered : CustomerType.Guest).ToString(),
                ResolveName(r.UserId, customers, r.Guest),
                ResolveEmail(r.UserId, customers, r.Guest),
                r.PaymentMethod.ToString()))
            .ToList();

        return new PagedResult<OrderListItem>(items, request.Page, request.PageSize, total);
    }

    /// <summary>
    /// Reads one order.
    /// <para>
    /// <paramref name="restrictToUserId"/> is applied as an explicit <c>WHERE UserId = ...</c>,
    /// not by trusting a client-supplied id (spec §7.5). A customer asking for someone
    /// else's order gets a 404, which also avoids confirming that the order exists.
    /// </para>
    /// </summary>
    internal async Task<OrderDetail> GetOrderAsync(
        Guid orderId,
        Guid? restrictToUserId,
        CancellationToken cancellationToken)
    {
        var query = db.Orders.Where(o => o.Id == orderId);

        if (restrictToUserId is { } userId)
        {
            query = query.Where(o => o.UserId == userId);
        }

        var order = await query
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Order", orderId);

        var guest = order.GuestCheckoutProfileId is { } guestId
            ? await db.GuestCheckoutProfiles
                .Where(g => g.Id == guestId)
                .Select(g => new GuestRef(g.FullName, g.Email))
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var customers = await ResolveCustomersAsync([order.UserId], cancellationToken);

        return new OrderDetail(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.SubTotal,
            order.DiscountTotal,
            order.TaxTotal,
            order.ShippingTotal,
            order.GrandTotal,
            order.CurrencyCode,
            order.AppliedOfferCode,
            order.PaymentMethod.ToString(),
            order.PaymentStatus.ToString(),
            order.PlacedUtc,
            order.Notes,
            (order.UserId is not null ? CustomerType.Registered : CustomerType.Guest).ToString(),
            ResolveName(order.UserId, customers, guest),
            ResolveEmail(order.UserId, customers, guest),
            new AddressResponse(
                order.ShippingAddress.FullName,
                order.ShippingAddress.Line1,
                order.ShippingAddress.Line2,
                order.ShippingAddress.City,
                order.ShippingAddress.State,
                order.ShippingAddress.PostalCode,
                order.ShippingAddress.Country,
                order.ShippingAddress.PhoneNumber),
            [.. order.Items.Select(i => new OrderItemResponse(
                i.ProductId,
                i.ProductNameSnapshot,
                i.SkuSnapshot,
                i.UnitPrice,
                i.Quantity,
                i.DiscountAmount,
                i.LineTotal))],
            [.. order.StatusHistory
                .OrderBy(h => h.ChangedUtc)
                .Select(h => new OrderStatusHistoryResponse(
                    h.FromStatus?.ToString(),
                    h.ToStatus.ToString(),
                    h.ChangedUtc,
                    h.ChangedByUserId,
                    h.Reason))]);
    }

    /// <summary>Admin status change (spec §9.3). Writes an <c>OrderStatusHistory</c> row.</summary>
    internal async Task<OrderDetail> ChangeStatusAsync(
        Guid orderId,
        ChangeOrderStatusRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        var order = await TrackedOrderAsync(orderId, cancellationToken);
        var wasCancelled = order.Status == OrderStatus.Cancelled;

        order.ChangeStatus(request.Status, clock.UtcNow, actingUserId, request.Reason);
        await db.SaveChangesAsync(cancellationToken);

        if (!wasCancelled && order.Status == OrderStatus.Cancelled)
        {
            await ReleaseStockAsync(order, cancellationToken);
        }

        return await GetOrderAsync(orderId, restrictToUserId: null, cancellationToken);
    }

    /// <summary>
    /// Customer-initiated cancellation (ADR-003): allowed while Pending or Confirmed,
    /// admin-only afterwards. Ownership is enforced by the query, not by the request body.
    /// </summary>
    internal async Task<OrderDetail> CancelOwnOrderAsync(
        Guid orderId,
        Guid userId,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken)
            ?? throw NotFoundException.For("Order", orderId);

        order.CancelByCustomer(clock.UtcNow, userId, request.Reason);
        await db.SaveChangesAsync(cancellationToken);

        await ReleaseStockAsync(order, cancellationToken);

        return await GetOrderAsync(orderId, userId, cancellationToken);
    }

    /// <summary>Returns reserved stock to the shelf through the same seam checkout used.</summary>
    private async Task ReleaseStockAsync(Order order, CancellationToken cancellationToken) =>
        await catalog.ReleaseStockAsync(
            [.. order.Items.Select(i => new StockReservationLine(i.ProductId, i.Quantity))],
            cancellationToken);

    private async Task<Order> TrackedOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await db.Orders
            .AsTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
        ?? throw NotFoundException.For("Order", orderId);

    private IQueryable<Order> Filtered(OrderFilter filter, Guid? restrictToUserId)
    {
        var query = db.Orders.AsQueryable();

        if (restrictToUserId is { } userId)
        {
            query = query.Where(o => o.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();

            // Order number or the guest's email. A registered customer's email lives in
            // another schema, so it cannot be searched here - the admin UI searches users
            // separately and filters by the resulting id.
            query = query.Where(o =>
                o.OrderNumber.StartsWith(term)
                || db.GuestCheckoutProfiles.Any(g => g.Id == o.GuestCheckoutProfileId && g.Email.StartsWith(term)));
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            query = query.Where(o => statuses.Contains(o.Status));
        }

        if (filter.DateFrom is { } from)
        {
            query = query.Where(o => o.PlacedUtc >= from);
        }

        if (filter.DateTo is { } to)
        {
            query = query.Where(o => o.PlacedUtc <= to);
        }

        if (filter.MinTotal is { } minTotal)
        {
            query = query.Where(o => o.GrandTotal >= minTotal);
        }

        if (filter.MaxTotal is { } maxTotal)
        {
            query = query.Where(o => o.GrandTotal <= maxTotal);
        }

        if (filter.CustomerType is { } customerType)
        {
            query = customerType == CustomerType.Registered
                ? query.Where(o => o.UserId != null)
                : query.Where(o => o.GuestCheckoutProfileId != null);
        }

        if (filter.PaymentMethod is { } paymentMethod)
        {
            query = query.Where(o => o.PaymentMethod == paymentMethod);
        }

        return query;
    }

    private static IQueryable<Order> Sort(IQueryable<Order> query, PagedRequest request) =>
        request.SortField switch
        {
            "placedUtc" when !request.SortDescending => query.OrderBy(o => o.PlacedUtc).ThenBy(o => o.Id),
            "grandTotal" when request.SortDescending => query.OrderByDescending(o => o.GrandTotal).ThenBy(o => o.Id),
            "grandTotal" => query.OrderBy(o => o.GrandTotal).ThenBy(o => o.Id),
            "orderNumber" => query.OrderBy(o => o.OrderNumber).ThenBy(o => o.Id),
            // Newest first by default, with a tiebreaker so paging cannot skip or repeat.
            _ => query.OrderByDescending(o => o.PlacedUtc).ThenBy(o => o.Id),
        };

    /// <summary>
    /// Resolves registered-customer names in one batched Contracts call (spec §8.4).
    /// One call per page, not one per row - that is the N+1 the boundary would otherwise
    /// invite.
    /// </summary>
    private async Task<Dictionary<Guid, UserSummaryDto>> ResolveCustomersAsync(
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var summaries = await identity.GetUserSummariesAsync(ids, cancellationToken);

        return summaries.ToDictionary(s => s.Id);
    }

    private static string ResolveName(Guid? userId, Dictionary<Guid, UserSummaryDto> customers, GuestRef? guest) =>
        userId is { } id && customers.TryGetValue(id, out var summary)
            ? summary.FullName
            : guest?.FullName ?? "(unknown)";

    private static string ResolveEmail(Guid? userId, Dictionary<Guid, UserSummaryDto> customers, GuestRef? guest) =>
        userId is { } id && customers.TryGetValue(id, out var summary)
            ? summary.Email
            : guest?.Email ?? string.Empty;

    /// <summary>Intermediate row shape, before customer identity is resolved.</summary>
    private sealed record OrderRow(
        Guid Id,
        string OrderNumber,
        OrderStatus Status,
        decimal GrandTotal,
        string CurrencyCode,
        DateTime PlacedUtc,
        int ItemCount,
        Guid? UserId,
        PaymentMethod PaymentMethod,
        GuestRef? Guest);

    private sealed record GuestRef(string FullName, string Email);
}
