using ShopHub.Modules.Catalog.Domain;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.UnitTests.Catalog;

/// <summary>
/// Spec §12 names "pricing/discount maths" as required unit coverage. Money rules are the
/// ones where a rounding slip is invisible until it reaches an invoice.
/// </summary>
public sealed class OfferTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static Offer Percentage(decimal percent, decimal? minimum = null, int? maxRedemptions = null) =>
        Offer.Create("SAVE", "Percent off", DiscountType.Percentage, percent, Now.AddDays(-1), Now.AddDays(30), minimum, maxRedemptions);

    private static Offer Fixed(decimal amount, decimal? minimum = null) =>
        Offer.Create("FLAT", "Flat off", DiscountType.FixedAmount, amount, Now.AddDays(-1), Now.AddDays(30), minimum);

    [Fact]
    public void Create_UppercasesTheCode()
    {
        var offer = Offer.Create("save10", "Ten off", DiscountType.Percentage, 10m, Now, Now.AddDays(1));

        Assert.Equal("SAVE10", offer.Code);
    }

    [Fact]
    public void PercentageDiscount_IsRoundedToTwoDecimals()
    {
        var offer = Percentage(10m);

        // 10% of 99.99 is 9.999, which must land on a cent boundary.
        Assert.Equal(10.00m, offer.CalculateDiscount(99.99m));
    }

    [Fact]
    public void PercentageDiscount_RoundsHalfAwayFromZero()
    {
        var offer = Percentage(50m);

        // 50% of 0.05 is 0.025 - banker's rounding would give 0.02.
        Assert.Equal(0.03m, offer.CalculateDiscount(0.05m));
    }

    [Fact]
    public void FixedDiscount_IsTheFlatAmount()
    {
        var offer = Fixed(25m);

        Assert.Equal(25m, offer.CalculateDiscount(200m));
    }

    /// <summary>
    /// The clamp that stops a fixed-amount offer larger than the cart producing a negative
    /// total - which would otherwise be a refund the customer never paid for.
    /// </summary>
    [Fact]
    public void FixedDiscount_NeverExceedsTheSubtotal()
    {
        var offer = Fixed(100m);

        Assert.Equal(30m, offer.CalculateDiscount(30m));
    }

    [Fact]
    public void Discount_IsZeroBelowTheMinimumOrderAmount()
    {
        var offer = Fixed(25m, minimum: 200m);

        Assert.Equal(0m, offer.CalculateDiscount(199.99m));
        Assert.Equal(25m, offer.CalculateDiscount(200m));
    }

    [Fact]
    public void IsRedeemable_OnlyInsideTheDateWindow()
    {
        var offer = Percentage(10m);

        Assert.False(offer.IsRedeemable(Now.AddDays(-2)));
        Assert.True(offer.IsRedeemable(Now));
        Assert.False(offer.IsRedeemable(Now.AddDays(31)));
    }

    [Fact]
    public void IsRedeemable_IsFalseWhenInactive()
    {
        var offer = Percentage(10m);
        offer.SetActive(false);

        Assert.False(offer.IsRedeemable(Now));
    }

    [Fact]
    public void Redemptions_StopAtTheCap()
    {
        var offer = Percentage(10m, maxRedemptions: 2);

        offer.Redeem(Now);
        offer.Redeem(Now);

        Assert.False(offer.IsRedeemable(Now));
        Assert.Throws<DomainRuleException>(() => offer.Redeem(Now));
        Assert.Equal(2, offer.RedemptionCount);
    }

    [Fact]
    public void Create_RefusesAPercentageAbove100() =>
        Assert.Throws<DomainRuleException>(() =>
            Offer.Create("X", "Too much", DiscountType.Percentage, 101m, Now, Now.AddDays(1)));

    [Fact]
    public void Create_RefusesANonPositiveValue() =>
        Assert.Throws<DomainRuleException>(() =>
            Offer.Create("X", "Nothing", DiscountType.FixedAmount, 0m, Now, Now.AddDays(1)));

    [Fact]
    public void Create_RefusesAnInvertedDateWindow() =>
        Assert.Throws<DomainRuleException>(() =>
            Offer.Create("X", "Backwards", DiscountType.Percentage, 10m, Now.AddDays(5), Now));
}
