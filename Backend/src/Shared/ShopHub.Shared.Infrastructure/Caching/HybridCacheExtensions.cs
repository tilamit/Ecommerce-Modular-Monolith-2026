using Microsoft.Extensions.Caching.Hybrid;

namespace ShopHub.Shared.Infrastructure.Caching;

/// <summary>
/// Cache reads whose factory is deliberately not tied to the caller's request.
/// </summary>
/// <remarks>
/// <see cref="HybridCache"/> is stampede-protected, so concurrent callers for one key join a
/// single execution of the factory. That work is shared and passing
/// <c>HttpContext.RequestAborted</c> into it hands ownership to whichever caller started it:
/// the abandoned work is discarded, the scoped <c>DbContext</c> is disposed while joined
/// callers still await it and the cancellation surfaces inside the cache internals. The
/// factory therefore runs to completion. These are short reads behind a TTL and a query
/// that genuinely hangs is bounded by the database command timeout.
/// </remarks>
public static class HybridCacheExtensions
{
    /// <summary>
    /// Reads a shared, cacheable value, running the factory to completion even if the
    /// caller goes away. Prefer this over <see cref="HybridCache.GetOrCreateAsync"/> for
    /// anything keyed on something other than the individual request.
    /// </summary>
    public static ValueTask<T> GetOrCreateSharedAsync<TState, T>(
        this HybridCache cache,
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(cache);

        return cache.GetOrCreateAsync(key, state, factory, options, tags, CancellationToken.None);
    }
}
