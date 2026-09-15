using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Ordering.Domain;
using ShopHub.Modules.Ordering.Infrastructure;
using ShopHub.Modules.Ordering.Persistence;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Ordering.Features.Carts;

/// <summary>
/// The signed-in customer's saved cart (spec §8.4 state 2) and the merge from a
/// localStorage cart on login (state 3).
/// </summary>
internal sealed class CartService(OrderingDbContext db, ICartPricer pricer, IClock clock)
{
    /// <summary>
    /// Spec §8.4: "ignore a repeat within 5 minutes". Long enough to cover a retry after a
    /// network blip, short enough that a genuine second merge later still works.
    /// </summary>
    private static readonly TimeSpan MergeIdempotencyWindow = TimeSpan.FromMinutes(5);

    internal async Task<CartResponse> GetCartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cart = await FindActiveCartAsync(userId, cancellationToken);

        return cart is null
            ? Empty()
            : await PriceCartAsync(cart, cancellationToken);
    }

    internal async Task<CartResponse> AddItemAsync(
        Guid userId,
        AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var cart = await GetOrCreateActiveCartAsync(userId, cancellationToken);

        // Price first: this both validates that the product is real and available, and
        // supplies the display snapshot. A caller cannot add a deleted product.
        var priced = await pricer.PriceAsync([new CartLineRequest(request.ProductId, request.Quantity)], PricingMode.CartDisplay, cancellationToken);

        if (priced.Lines.Count == 0)
        {
            return await PriceCartAsync(cart, cancellationToken, priced.Skipped);
        }

        var line = priced.Lines[0];
        cart.AddItem(line.ProductId, request.Quantity, line.UnitPrice, clock.UtcNow);

        await db.SaveChangesAsync(cancellationToken);

        return await PriceCartAsync(cart, cancellationToken);
    }

    internal async Task<CartResponse> SetQuantityAsync(
        Guid userId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var cart = await GetOrCreateActiveCartAsync(userId, cancellationToken);

        cart.SetQuantity(productId, quantity, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return await PriceCartAsync(cart, cancellationToken);
    }

    internal async Task<CartResponse> RemoveItemAsync(Guid userId, Guid productId, CancellationToken cancellationToken)
    {
        var cart = await GetOrCreateActiveCartAsync(userId, cancellationToken);

        cart.RemoveItem(productId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return await PriceCartAsync(cart, cancellationToken);
    }

    /// <summary>
    /// Merges a localStorage cart into the server cart on login (spec §8.4 state 3).
    /// <para>
    /// Sums quantities per product, clamps to available stock, drops products that are now
    /// inactive or deleted and reports them in <c>skipped[]</c> and re-prices everything
    /// server-side. Idempotent on <c>mergeToken</c>.
    /// </para>
    /// </summary>
    internal async Task<CartResponse> MergeAsync(Guid userId, MergeCartRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var cart = await GetOrCreateActiveCartAsync(userId, cancellationToken);

        // The idempotency guard. Without it, a retried request doubles every quantity -
        // which is the single most visible way a merge can go wrong.
        if (cart.HasAlreadyMerged(request.MergeToken, now, MergeIdempotencyWindow))
        {
            return await PriceCartAsync(cart, cancellationToken);
        }

        var incoming = request.Items.Where(i => i.Quantity > 0).ToArray();
        var priced = await pricer.PriceAsync(incoming, PricingMode.CartDisplay, cancellationToken);

        foreach (var line in priced.Lines)
        {
            cart.MergeItem(line.ProductId, line.Quantity, line.UnitPrice, now);
        }

        cart.RecordMerge(request.MergeToken, now);
        await db.SaveChangesAsync(cancellationToken);

        // Skipped lines are carried through to the response so the UI can explain why an
        // item the customer had in their basket is not there any more.
        return await PriceCartAsync(cart, cancellationToken, priced.Skipped);
    }

    /// <summary>
    /// Prices an anonymous cart without persisting anything (spec §8.4 state 1: "re-price on
    /// the server on every cart render").
    /// </summary>
    internal async Task<CartResponse> PriceAnonymousAsync(PriceCartRequest request, CancellationToken cancellationToken)
    {
        var priced = await pricer.PriceAsync(request.Items, PricingMode.CartDisplay, cancellationToken);

        return ToResponse(cartId: null, priced, priced.Skipped);
    }

    /// <summary>Marks the cart converted after checkout, so it is not reused.</summary>
    internal async Task<Cart?> ConvertActiveCartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cart = await FindActiveCartAsync(userId, cancellationToken);

        if (cart is null)
        {
            return null;
        }

        cart.MarkConverted(clock.UtcNow);

        return cart;
    }

    internal async Task<Cart?> FindActiveCartAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Carts
            .AsTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CartStatus.Active, cancellationToken);

    private async Task<Cart> GetOrCreateActiveCartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await FindActiveCartAsync(userId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var cart = Cart.ForUser(userId, clock.UtcNow);

        db.Carts.Add(cart);
        await db.SaveChangesAsync(cancellationToken);

        return cart;
    }

    /// <summary>
    /// Re-prices the persisted cart from Catalog. The stored <c>UnitPriceSnapshot</c> is
    /// display-only and is never used for money (spec §8.4).
    /// </summary>
    private async Task<CartResponse> PriceCartAsync(
        Cart cart,
        CancellationToken cancellationToken,
        IReadOnlyList<SkippedLineResponse>? additionalSkipped = null)
    {
        var lines = cart.Items.Select(i => new CartLineRequest(i.ProductId, i.Quantity)).ToArray();
        var priced = await pricer.PriceAsync(lines, PricingMode.CartDisplay, cancellationToken);

        var skipped = additionalSkipped is null
            ? priced.Skipped
            : [.. priced.Skipped.Concat(additionalSkipped).DistinctBy(s => s.ProductId)];

        return ToResponse(cart.Id, priced, skipped);
    }

    private static CartResponse ToResponse(Guid? cartId, PricedCart priced, IReadOnlyList<SkippedLineResponse> skipped) =>
        new(
            cartId,
            [.. priced.Lines.Select(l => new CartLineResponse(
                l.ProductId,
                l.Name,
                l.Sku,
                l.UnitPrice,
                l.Quantity,
                l.LineTotal,
                l.AvailableStock,
                l.QuantityWasClamped))],
            skipped,
            priced.SubTotal,
            priced.CurrencyCode,
            priced.TotalQuantity);

    private static CartResponse Empty() => new(null, [], [], 0m, "USD", 0);
}
