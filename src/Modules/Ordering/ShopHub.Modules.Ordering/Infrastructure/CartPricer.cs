using ShopHub.Modules.Catalog.Contracts;
using ShopHub.Modules.Ordering.Features.Carts;

namespace ShopHub.Modules.Ordering.Infrastructure;

/// <summary>
/// Re-prices a set of product/quantity pairs against Catalog (spec §8.4).
/// <para>
/// This exists because <b>the client's prices are never trusted</b>. The anonymous cart in
/// localStorage stores only <c>productId</c> and <c>quantity</c>; anything else would be a
/// price-tampering hole. Every cart render and the checkout itself run through here.
/// </para>
/// </summary>
internal interface ICartPricer
{
    Task<PricedCart> PriceAsync(
        IReadOnlyCollection<CartLineRequest> lines,
        PricingMode mode,
        CancellationToken cancellationToken);
}

/// <summary>
/// Why the cart is being priced, which decides how an under-stocked line is treated.
/// </summary>
internal enum PricingMode
{
    /// <summary>
    /// Rendering a cart. Quantities are clamped to what is on the shelf and flagged, so the
    /// UI can say "only 3 left" while the customer is still deciding.
    /// </summary>
    CartDisplay = 1,

    /// <summary>
    /// Placing an order. Quantities are <b>not</b> clamped: the requested amount is carried
    /// through so reservation fails and the customer is told what changed (ADR-002).
    /// Clamping here would quietly hand them fewer items than the total they approved.
    /// </summary>
    Checkout = 2,
}

/// <summary>The outcome of pricing: the lines that survived, and the ones that did not.</summary>
internal sealed record PricedCart(
    IReadOnlyList<PricedLine> Lines,
    IReadOnlyList<SkippedLineResponse> Skipped,
    decimal SubTotal,
    string CurrencyCode)
{
    public int TotalQuantity => Lines.Sum(l => l.Quantity);
}

internal sealed record PricedLine(
    Guid ProductId,
    string Name,
    string Sku,
    decimal UnitPrice,
    int Quantity,
    int AvailableStock,
    bool QuantityWasClamped)
{
    public decimal LineTotal => Math.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Prices through <see cref="ICatalogModuleApi"/> - the only way Ordering can learn a
/// price, since <c>catalog.Products</c> is unreachable from here by construction.
/// </summary>
internal sealed class CartPricer(ICatalogModuleApi catalog) : ICartPricer
{
    private const string DefaultCurrency = "USD";

    internal const string ReasonNotFound = "not_found";
    internal const string ReasonInactive = "inactive";
    internal const string ReasonOutOfStock = "out_of_stock";

    public async Task<PricedCart> PriceAsync(
        IReadOnlyCollection<CartLineRequest> lines,
        PricingMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return new PricedCart([], [], 0m, DefaultCurrency);
        }

        // Sum duplicate lines first (spec §8.4 merge strategy), so stock is checked against
        // the combined quantity rather than each half independently.
        var requested = lines
            .Where(l => l.Quantity > 0)
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        // One batched call, not one per line. This is where the boundary would otherwise
        // become an N+1.
        var snapshots = await catalog.GetProductSnapshotsAsync(requested.Keys.ToArray(), cancellationToken);
        var byId = snapshots.ToDictionary(s => s.Id);

        var priced = new List<PricedLine>();
        var skipped = new List<SkippedLineResponse>();

        foreach (var (productId, quantity) in requested)
        {
            if (!byId.TryGetValue(productId, out var snapshot))
            {
                skipped.Add(new SkippedLineResponse(productId, null, ReasonNotFound));
                continue;
            }

            if (!snapshot.IsActive)
            {
                skipped.Add(new SkippedLineResponse(productId, snapshot.Name, ReasonInactive));
                continue;
            }

            if (snapshot.StockQuantity <= 0)
            {
                skipped.Add(new SkippedLineResponse(productId, snapshot.Name, ReasonOutOfStock));
                continue;
            }

            // Clamp only when rendering a cart. At checkout the requested quantity is
            // carried through unchanged so the reservation refuses it and the customer
            // gets a 409 naming the line, rather than a smaller order they never chose.
            var clamped = mode == PricingMode.CartDisplay
                ? Math.Min(quantity, snapshot.StockQuantity)
                : quantity;

            priced.Add(new PricedLine(
                snapshot.Id,
                snapshot.Name,
                snapshot.Sku,
                snapshot.Price,
                clamped,
                snapshot.StockQuantity,
                QuantityWasClamped: clamped != quantity));
        }

        var subTotal = Math.Round(priced.Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);

        // Multi-currency is out of scope (spec A9), so the first line's currency stands for
        // the cart. If mixed currencies ever become possible this is where it breaks loudly.
        var currency = priced.Count > 0
            ? snapshots.First(s => s.Id == priced[0].ProductId).CurrencyCode
            : DefaultCurrency;

        return new PricedCart(priced, skipped, subTotal, currency);
    }
}
