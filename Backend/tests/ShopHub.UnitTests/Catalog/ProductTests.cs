using ShopHub.Modules.Catalog.Domain;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.UnitTests.Catalog;

/// <summary>
/// Stock rules live in the domain so they hold for every caller - the reservation path in
/// spec §8.4 depends on <see cref="Product.Reserve"/> refusing rather than clamping.
/// </summary>
public sealed class ProductTests
{
    private static Product Create(int stock = 10) =>
        Product.Create(Guid.CreateVersion7(), "sku-1", "Laptop Pro", "laptop-pro", 999.99m, stock);

    [Fact]
    public void Create_NormalizesSkuAndSlug()
    {
        var product = Product.Create(Guid.CreateVersion7(), " sku-1 ", " Laptop Pro ", "Laptop Pro!", 10m, 1);

        Assert.Equal("SKU-1", product.Sku);
        Assert.Equal("Laptop Pro", product.Name);
        Assert.Equal("laptop-pro", product.Slug);
    }

    [Fact]
    public void Create_StartsActive()
    {
        var product = Create();

        Assert.True(product.IsActive);
        Assert.True(product.IsInStock);
    }

    [Fact]
    public void Reserve_DecrementsStock()
    {
        var product = Create(stock: 10);

        product.Reserve(3);

        Assert.Equal(7, product.StockQuantity);
    }

    /// <summary>
    /// Refuses rather than clamping. A silent partial reservation would let an order ship
    /// fewer items than were paid for (ADR-002).
    /// </summary>
    [Fact]
    public void Reserve_MoreThanAvailable_Throws()
    {
        var product = Create(stock: 2);

        var exception = Assert.Throws<DomainRuleException>(() => product.Reserve(3));

        Assert.Equal("insufficient_stock", exception.Code);
        Assert.Equal(2, product.StockQuantity);
    }

    [Fact]
    public void Reserve_ExactlyAllStock_IsAllowed()
    {
        var product = Create(stock: 3);

        product.Reserve(3);

        Assert.Equal(0, product.StockQuantity);
        Assert.False(product.IsInStock);
    }

    [Fact]
    public void Reserve_FromAnInactiveProduct_Throws()
    {
        var product = Create();
        product.Deactivate();

        var exception = Assert.Throws<DomainRuleException>(() => product.Reserve(1));

        Assert.Equal("product_unavailable", exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_NonPositiveQuantity_Throws(int quantity)
    {
        var product = Create();

        var exception = Assert.Throws<DomainRuleException>(() => product.Reserve(quantity));

        Assert.Equal("quantity_invalid", exception.Code);
    }

    [Fact]
    public void Release_ReturnsStockToTheShelf()
    {
        var product = Create(stock: 10);

        product.Reserve(4);
        product.Release(4);

        Assert.Equal(10, product.StockQuantity);
    }

    [Fact]
    public void AdjustStock_RefusesNegativeValues()
    {
        var product = Create();

        var exception = Assert.Throws<DomainRuleException>(() => product.AdjustStock(-1));

        Assert.Equal("stock_negative", exception.Code);
    }

    /// <summary>
    /// A "was" price at or below the current price would render as a nonsense discount, so
    /// the domain rejects it rather than letting the UI display it.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(50)]
    public void Update_RefusesACompareAtPriceNotAboveThePrice(decimal compareAt)
    {
        var product = Create();

        var exception = Assert.Throws<DomainRuleException>(() => product.Update(
            Guid.CreateVersion7(),
            "sku-1",
            "Laptop Pro",
            "laptop-pro",
            null,
            null,
            price: 100m,
            compareAtPrice: compareAt,
            "USD",
            5,
            isFeatured: false));

        Assert.Equal("compare_at_price_invalid", exception.Code);
    }

    [Theory]
    [InlineData("Laptop Pro", "laptop-pro")]
    [InlineData("  Spaced   Out  ", "spaced-out")]
    [InlineData("Symbols!@#Here", "symbols-here")]
    [InlineData("ALL CAPS", "all-caps")]
    [InlineData("trailing---", "trailing")]
    public void Slugify_ProducesUrlSafeLowercase(string input, string expected) =>
        Assert.Equal(expected, Category.Slugify(input));
}
