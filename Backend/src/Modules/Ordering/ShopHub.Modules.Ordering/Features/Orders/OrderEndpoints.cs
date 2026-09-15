using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShopHub.Modules.Ordering.Domain;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Ordering.Features.Orders;

/// <summary><c>/api/v1/orders</c> (spec §9.3).</summary>
internal static class OrderEndpoints
{
    internal static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/orders")
            .WithTags("Orders")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        // "/me" routes are declared first so the literal segment is not captured as an id.
        group.MapGet("/me", GetMyOrdersAsync).RequireAuthorization(Permissions.OrdersReadOwn);
        group.MapGet("/me/{id:guid}", GetMyOrderAsync).RequireAuthorization(Permissions.OrdersReadOwn);

        group.MapPost("/me/{id:guid}/cancel", CancelMyOrderAsync)
            .RequireAuthorization(Permissions.OrdersReadOwn)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapGet("/", GetOrdersAsync).RequireAuthorization(Permissions.OrdersReadAll);
        group.MapGet("/{id:guid}", GetOrderAsync).RequireAuthorization(Permissions.OrdersReadAll);

        group.MapPatch("/{id:guid}/status", ChangeStatusAsync)
            .RequireAuthorization(Permissions.OrdersManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        return endpoints;
    }

    /// <summary>
    /// The customer's own orders. Scoped server-side to <c>ICurrentUser.Id</c> - the
    /// endpoint accepts no user id, so there is nothing for a caller to tamper with
    /// (spec §7.5).
    /// </summary>
    private static async Task<IResult> GetMyOrdersAsync(
        OrderService orders,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize,
        string? sort = null,
        string? search = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null)
    {
        var paging = new PagedRequest(page, pageSize, sort, search);
        var filter = new OrderFilter(search, null, dateFrom, dateTo, null, null, null, null);

        return Results.Ok(await orders.GetOrdersAsync(paging, filter, currentUser.RequiredId, cancellationToken));
    }

    private static async Task<IResult> GetMyOrderAsync(
        Guid id,
        OrderService orders,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await orders.GetOrderAsync(id, currentUser.RequiredId, cancellationToken));

    private static async Task<IResult> CancelMyOrderAsync(
        Guid id,
        CancelOrderRequest request,
        OrderService orders,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await orders.CancelOwnOrderAsync(id, currentUser.RequiredId, request, cancellationToken));

    private static async Task<IResult> GetOrdersAsync(
        OrderService orders,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize,
        string? sort = null,
        string? search = null,
        [Microsoft.AspNetCore.Mvc.FromQuery] OrderStatus[]? status = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        decimal? minTotal = null,
        decimal? maxTotal = null,
        CustomerType? customerType = null,
        PaymentMethod? paymentMethod = null)
    {
        var paging = new PagedRequest(page, pageSize, sort, search);

        var filter = new OrderFilter(
            search,
            status,
            dateFrom,
            dateTo,
            minTotal,
            maxTotal,
            customerType,
            paymentMethod);

        return Results.Ok(await orders.GetOrdersAsync(paging, filter, restrictToUserId: null, cancellationToken));
    }

    private static async Task<IResult> GetOrderAsync(Guid id, OrderService orders, CancellationToken cancellationToken) =>
        Results.Ok(await orders.GetOrderAsync(id, restrictToUserId: null, cancellationToken));

    private static async Task<IResult> ChangeStatusAsync(
        Guid id,
        ChangeOrderStatusRequest request,
        OrderService orders,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await orders.ChangeStatusAsync(id, request, currentUser.RequiredId, cancellationToken));
}
