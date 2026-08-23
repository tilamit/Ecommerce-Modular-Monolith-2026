using ShopHub.Modules.Ordering.Domain;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.UnitTests.Ordering;

/// <summary>
/// Spec §12 names "cart merge rules" as required unit coverage. The merge is the operation
/// most likely to silently corrupt a customer's basket.
/// </summary>
public sealed class CartTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private static Cart NewCart() => Cart.ForUser(Guid.CreateVersion7(), Now);

    [Fact]
    public void ANewCart_IsActiveAndEmpty()
    {
        var cart = NewCart();

        Assert.Equal(CartStatus.Active, cart.Status);
        Assert.Empty(cart.Items);
        Assert.Equal(0, cart.TotalQuantity);
    }

    /// <summary>A signed-in cart is the user's saved cart; only the anonymous one expires (spec §8.4).</summary>
    [Fact]
    public void ASignedInCart_DoesNotExpire() => Assert.Null(NewCart().ExpiresUtc);

    [Fact]
    public void AddingTheSameProductTwice_IncreasesOneLine()
    {
        var cart = NewCart();
        var productId = Guid.CreateVersion7();

        cart.AddItem(productId, 2, 10m, Now);
        cart.AddItem(productId, 3, 10m, Now);

        var line = Assert.Single(cart.Items);
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public void SetQuantity_ToZeroRemovesTheLine()
    {
        var cart = NewCart();
        var productId = Guid.CreateVersion7();

        cart.AddItem(productId, 2, 10m, Now);
        cart.SetQuantity(productId, 0, Now);

        Assert.Empty(cart.Items);
    }

    [Fact]
    public void SetQuantity_OnAMissingProductThrowsNotFound()
    {
        var cart = NewCart();

        Assert.Throws<NotFoundException>(() => cart.SetQuantity(Guid.CreateVersion7(), 1, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AddItem_RefusesANonPositiveQuantity(int quantity)
    {
        var cart = NewCart();

        var exception = Assert.Throws<DomainRuleException>(
            () => cart.AddItem(Guid.CreateVersion7(), quantity, 10m, Now));

        Assert.Equal("quantity_invalid", exception.Code);
    }

    /// <summary>Spec §8.4 merge strategy: <b>sum</b> quantities per product.</summary>
    [Fact]
    public void Merge_SumsQuantitiesPerProduct()
    {
        var cart = NewCart();
        var productId = Guid.CreateVersion7();

        cart.AddItem(productId, 2, 10m, Now);
        cart.MergeItem(productId, 3, 10m, Now);

        Assert.Equal(5, Assert.Single(cart.Items).Quantity);
    }

    // --- idempotency (spec §8.4: "ignore a repeat within 5 minutes") --------

    [Fact]
    public void AFreshMergeToken_IsNotTreatedAsAlreadyApplied()
    {
        var cart = NewCart();

        Assert.False(cart.HasAlreadyMerged(Guid.CreateVersion7(), Now, Window));
    }

    [Fact]
    public void TheSameMergeToken_IsIgnoredInsideTheWindow()
    {
        var cart = NewCart();
        var token = Guid.CreateVersion7();

        cart.RecordMerge(token, Now);

        Assert.True(cart.HasAlreadyMerged(token, Now.AddMinutes(4), Window));
    }

    /// <summary>
    /// The window is a retry guard, not a permanent lock: a genuine second merge with the
    /// same token later must still apply.
    /// </summary>
    [Fact]
    public void TheSameMergeToken_AppliesAgainAfterTheWindow()
    {
        var cart = NewCart();
        var token = Guid.CreateVersion7();

        cart.RecordMerge(token, Now);

        Assert.False(cart.HasAlreadyMerged(token, Now.AddMinutes(6), Window));
    }

    [Fact]
    public void ADifferentMergeToken_IsNeverIgnored()
    {
        var cart = NewCart();

        cart.RecordMerge(Guid.CreateVersion7(), Now);

        Assert.False(cart.HasAlreadyMerged(Guid.CreateVersion7(), Now, Window));
    }

    [Fact]
    public void MarkConverted_MakesTheCartUnusable()
    {
        var cart = NewCart();

        cart.MarkConverted(Now);

        Assert.Equal(CartStatus.Converted, cart.Status);
        Assert.Throws<DomainRuleException>(() => cart.MarkConverted(Now));
    }

    [Fact]
    public void Clear_EmptiesTheCart()
    {
        var cart = NewCart();
        cart.AddItem(Guid.CreateVersion7(), 1, 5m, Now);

        cart.Clear(Now);

        Assert.Empty(cart.Items);
    }
}
