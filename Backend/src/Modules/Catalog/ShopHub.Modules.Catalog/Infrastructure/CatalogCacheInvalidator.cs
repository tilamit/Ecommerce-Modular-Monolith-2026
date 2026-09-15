using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using ShopHub.Shared.Infrastructure.Caching;

namespace ShopHub.Modules.Catalog.Infrastructure;

/// <summary>
/// Group invalidation for catalog read models (spec §6.4).
/// <para>
/// Behind an interface so a write path can declare "this invalidates products" without
/// depending on the caching implementation and so the behaviour can be asserted in tests.
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
/// Shared shape of the storefront output-cache policies.
/// </summary>
internal static class StorefrontOutputCache
{
    /// <summary>
    /// Turns off output-cache request locking.
    /// </summary>
    /// <remarks>
    /// With locking on (the default), concurrent requests for one cache key join a single
    /// execution and receive whatever it buffered. If that request's caller hangs up, the
    /// joiners are answered 200 with an empty body and no exception is raised. React's
    /// development double-mount hits this on the storefront's first paint. HybridCache still
    /// deduplicates the database read, so only JSON serialisation duplicates on a miss.
    /// </remarks>
    internal static OutputCachePolicyBuilder WithoutJoinedRequests(this OutputCachePolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return policy.SetLocking(false);
    }
}

/// <summary>
/// Invalidates <b>both</b> caching layers in front of catalog reads: HybridCache holds the
/// query result, OutputCache holds the serialized response for anonymous storefront GETs
/// (spec §6.4). Evicting only the first leaves the second serving a stale body for its TTL.
/// </summary>
/// <remarks>
/// <c>RemoveByTagAsync</c> is a logical invalidation rather than an immediate physical
/// eviction: the tag is stamped and older entries are treated as misses on the next read.
/// Checked against Microsoft.Extensions.Caching.Hybrid 10.9.0, which spec §16 flags as
/// version-dependent. A read after this call never returns pre-invalidation data.
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
