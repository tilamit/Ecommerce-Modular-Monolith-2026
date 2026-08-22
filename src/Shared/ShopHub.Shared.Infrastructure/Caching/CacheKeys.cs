using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShopHub.Shared.Infrastructure.Caching;

/// <summary>
/// Cache key convention from spec §6.4: <c>&lt;module&gt;:&lt;entity&gt;:&lt;discriminator&gt;</c>.
/// <para>
/// Keys are built here rather than inline so a typo cannot silently create a second,
/// permanently-cold cache entry that nobody ever invalidates.
/// </para>
/// </summary>
public static class CacheKeys
{
    public static string For(string module, string entity, string discriminator) =>
        $"{module}:{entity}:{discriminator}";

    public static string For(string module, string entity, Guid id) =>
        $"{module}:{entity}:{id}";

    /// <summary>
    /// Stable short hash of a filter object, for list keys (spec §6.4).
    /// <para>
    /// Never build a key from raw user input: an unbounded string would let a caller fill
    /// the cache with single-use entries. Hashing bounds the key and makes it stable across
    /// property order.
    /// </para>
    /// </summary>
    public static string HashFilter<TFilter>(TFilter filter)
    {
        var json = JsonSerializer.Serialize(filter, StableJsonOptions);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexStringLower(digest)[..16];
    }

    private static readonly JsonSerializerOptions StableJsonOptions = new()
    {
        // Property order in the serialized form must not depend on reflection order,
        // or the same filter could hash two different ways across runs.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}

/// <summary>
/// Tags used for group invalidation via <c>RemoveByTagAsync</c> (spec §6.4).
/// </summary>
public static class CacheTags
{
    public const string Products = "catalog:products";

    public const string Categories = "catalog:categories";

    public const string Menus = "identity:menus";

    public const string Permissions = "identity:permissions";

    public const string Dashboards = "dashboard";
}
