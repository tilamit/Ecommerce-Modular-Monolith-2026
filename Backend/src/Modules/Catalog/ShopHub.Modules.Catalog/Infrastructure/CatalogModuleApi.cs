using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Catalog.Contracts;
using ShopHub.Modules.Catalog.Persistence;

namespace ShopHub.Modules.Catalog.Infrastructure;

/// <summary>
/// Catalog's implementation of its own Contracts interface (spec §4.3).
/// <para>
/// The extract-to-microservice seam: swapping this class for an HTTP client is the whole
/// change on Catalog's side, because no caller has ever seen anything but the DTOs.
/// </para>
/// </summary>
internal sealed class CatalogModuleApi(CatalogDbContext db, ICatalogCacheInvalidator cache) : ICatalogModuleApi
{
    public async Task<IReadOnlyList<ProductSnapshotDto>> GetProductSnapshotsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return [];
        }

        var ids = productIds.Distinct().ToArray();

        return await db.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProductSnapshotDto(
                p.Id,
                p.Name,
                p.Sku,
                p.Price,
                p.CurrencyCode,
                p.IsActive,
                p.StockQuantity))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// All-or-nothing reservation. Every line is validated before anything is decremented,
    /// so a partial failure leaves stock untouched rather than half-applied.
    /// </summary>
    public async Task<StockReservationResult> ReserveStockAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return StockReservationResult.Success();
        }

        // Merge duplicate lines first: two entries for the same product must be checked
        // against the combined quantity, not each half independently.
        var requested = lines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var ids = requested.Keys.ToArray();

        var products = await db.Products
            .AsTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var failures = new List<StockReservationFailure>();

        foreach (var (productId, quantity) in requested)
        {
            if (!products.TryGetValue(productId, out var product))
            {
                failures.Add(new StockReservationFailure(productId, "(unknown product)", "not_found", 0, quantity));
                continue;
            }

            if (!product.IsActive)
            {
                failures.Add(new StockReservationFailure(productId, product.Name, "inactive", product.StockQuantity, quantity));
                continue;
            }

            if (product.StockQuantity < quantity)
            {
                failures.Add(new StockReservationFailure(
                    productId,
                    product.Name,
                    "insufficient_stock",
                    product.StockQuantity,
                    quantity));
            }
        }

        if (failures.Count > 0)
        {
            return StockReservationResult.Failed(failures);
        }

        foreach (var (productId, quantity) in requested)
        {
            products[productId].Reserve(quantity);
        }

        // A concurrent reservation that wins the race causes a DbUpdateConcurrencyException
        // here, which the global handler maps to 409 (spec §6.1). That is the correct
        // answer: the customer should re-check availability rather than oversell.
        await db.SaveChangesAsync(cancellationToken);
        await cache.InvalidateProductsAsync(cancellationToken);

        return StockReservationResult.Success();
    }

    public async Task ReleaseStockAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return;
        }

        var requested = lines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var ids = requested.Keys.ToArray();

        var products = await db.Products
            .AsTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            product.Release(requested[product.Id]);
        }

        await db.SaveChangesAsync(cancellationToken);
        await cache.InvalidateProductsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveProductIdsAsync(int take, CancellationToken cancellationToken = default)
    {
        // Clamped, so a caller cannot ask for the whole table by passing int.MaxValue.
        var limit = Math.Clamp(take, 1, 500);

        return await db.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .Take(limit)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CatalogCountsDto> GetActiveCountsAsync(CancellationToken cancellationToken = default)
    {
        // Aggregated in SQL rather than by materialising rows and counting them (spec §10.1).
        var productCounts = await db.Products
            .GroupBy(p => p.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var activeCategories = await db.Categories.CountAsync(c => c.IsActive, cancellationToken);

        return new CatalogCountsDto(
            productCounts.FirstOrDefault(c => c.IsActive)?.Count ?? 0,
            productCounts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0,
            activeCategories);
    }
}
