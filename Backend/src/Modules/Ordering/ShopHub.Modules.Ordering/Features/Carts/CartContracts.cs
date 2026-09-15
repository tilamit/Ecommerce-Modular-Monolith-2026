namespace ShopHub.Modules.Ordering.Features.Carts;

/// <summary>
/// A priced cart. Every price here was resolved on the server from Catalog just now -
/// nothing the client sent is trusted for money (spec §8.4).
/// </summary>
internal sealed record CartResponse(
    Guid? CartId,
    IReadOnlyList<CartLineResponse> Items,
    IReadOnlyList<SkippedLineResponse> Skipped,
    decimal SubTotal,
    string CurrencyCode,
    int TotalQuantity);

internal sealed record CartLineResponse(
    Guid ProductId,
    string Name,
    string Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    int AvailableStock,
    bool QuantityWasClamped);

/// <summary>
/// A line that could not be honoured and why (spec §8.4: "return them in the response as
/// <c>skipped[]</c> so the UI can say why").
/// </summary>
internal sealed record SkippedLineResponse(Guid ProductId, string? Name, string Reason);

/// <summary>A line as the client sees it - product and quantity only, never a price.</summary>
internal sealed record CartLineRequest(Guid ProductId, int Quantity);

internal sealed record AddCartItemRequest(Guid ProductId, int Quantity);

internal sealed record UpdateCartItemRequest(int Quantity);

/// <summary>
/// Merge of a localStorage cart into the server cart on login (spec §8.4 state 3).
/// <para>
/// <see cref="MergeToken"/> is client-generated and makes the operation idempotent: a retry
/// after a network blip must not double quantities.
/// </para>
/// </summary>
internal sealed record MergeCartRequest(Guid MergeToken, IReadOnlyList<CartLineRequest> Items);

/// <summary>Re-prices an anonymous localStorage cart without persisting anything.</summary>
internal sealed record PriceCartRequest(IReadOnlyList<CartLineRequest> Items);
