using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using ShopHub.Shared.Infrastructure.Caching;

namespace ShopHub.Modules.Catalog.Infrastructure;

/// <summary>
/// Group invalidation for catalog read models (spec §6.4).
/// <para>
/// Behind an interface so a write path can declare "this invalidates products" without
/// depending on the caching implementation, and so the behaviour can be asserted in tests.
/// </para>
/// </summary>
internal interface ICatalogCacheInvalidator
{
    Task InvalidateProductsAsync(CancellationToken cancellationToken);

    Task InvalidateCategoriesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Output-cache tags, kept next to the HybridCache tags they shadow so the two cannot
/// drift apart.
/// </summary>
internal static class OutputCacheTags
{
    internal const string Products = "output:catalog:products";

    internal const string Categories = "output:catalog:categories";
}

/// <summary>
/// Invalidates <b>both</b> caching layers that sit in front of catalog reads.
/// </summary>
/// <remarks>
/// <para>
/// There are two, and forgetting either one produces stale data that looks like a caching
/// bug in the other. <c>HybridCache</c> holds the query result; <c>OutputCache</c> holds the
/// entire serialized HTTP response for anonymous storefront GETs (spec §6.4). Evicting only
/// the first leaves the second serving a stale body for the rest of its TTL - which is
/// exactly what the Phase 3 acceptance check caught before this eviction was added.
/// </para>
/// <para>
/// VERIFIED at install time (Microsoft.Extensions.Caching.Hybrid 10.9.0), because spec §16
/// flags it as version-dependent: <c>RemoveByTagAsync</c> is a <b>logical</b> invalidation,
/// not an immediate physical eviction - the tag is stamped and older entries are treated as
/// misses on the next read. Observed behaviour matches the contract that matters: a read
/// after this call never returns pre-invalidation data.
/// </para>
/// </remarks>
internal sealed class CatalogCacheInvalidator(HybridCache cache, IOutputCacheStore outputCache) : ICatalogCacheInvalidator
{
    public async Task InvalidateProductsAsync(CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(CacheTags.Products, cancellationToken);
        await outputCache.EvictByTagAsync(OutputCacheTags.Products, cancellationToken);
    }

    public async Task InvalidateCategoriesAsync(CancellationToken cancellationToken)
    {
        // A category change moves products between branches and can change what the
        // storefront grid shows, so both tags go on either layer.
        await cache.RemoveByTagAsync(CacheTags.Categories, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.Products, cancellationToken);

        await outputCache.EvictByTagAsync(OutputCacheTags.Categories, cancellationToken);
        await outputCache.EvictByTagAsync(OutputCacheTags.Products, cancellationToken);
    }
}
