using ShopHub.Shared.Infrastructure.Caching;

namespace ShopHub.UnitTests.Caching;

/// <summary>
/// Spec §6.4. The hash is what stands between a list cache and an attacker filling it
/// with single-use entries built from raw query strings, so its properties matter.
/// </summary>
public sealed class CacheKeyTests
{
    private sealed record ProductFilter(string? Search, int Page, int PageSize, bool? IsActive);

    [Fact]
    public void For_FollowsTheModuleEntityDiscriminatorConvention()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal($"catalog:product:{id}", CacheKeys.For("catalog", "product", id));
    }

    [Fact]
    public void HashFilter_IsStableForEqualFilters()
    {
        var a = new ProductFilter("laptop", 1, 20, true);
        var b = new ProductFilter("laptop", 1, 20, true);

        Assert.Equal(CacheKeys.HashFilter(a), CacheKeys.HashFilter(b));
    }

    [Fact]
    public void HashFilter_DiffersWhenAnyFieldDiffers()
    {
        var a = new ProductFilter("laptop", 1, 20, true);
        var b = new ProductFilter("laptop", 2, 20, true);

        Assert.NotEqual(CacheKeys.HashFilter(a), CacheKeys.HashFilter(b));
    }

    /// <summary>
    /// The key must stay bounded no matter what the caller sends, or an unbounded search
    /// term becomes an unbounded cache key.
    /// </summary>
    [Fact]
    public void HashFilter_IsFixedLengthEvenForHugeInput()
    {
        var huge = new ProductFilter(new string('x', 100_000), 1, 20, null);

        Assert.Equal(16, CacheKeys.HashFilter(huge).Length);
    }

    [Fact]
    public void HashFilter_IsLowercaseHex()
    {
        var hash = CacheKeys.HashFilter(new ProductFilter("laptop", 1, 20, true));

        Assert.Matches("^[0-9a-f]{16}$", hash);
    }
}
