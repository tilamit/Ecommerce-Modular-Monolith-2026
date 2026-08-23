using ShopHub.Shared.Kernel.Entities;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Catalog.Domain;

/// <summary>Percentage or fixed-amount discount (spec §8.2).</summary>
internal enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2,
}

/// <summary>
/// A discount code (spec §8.2). The redemption cap and the date window are enforced here
/// rather than at the call site, so an expired offer cannot be applied by a caller that
/// forgot to check.
/// </summary>
internal sealed class Offer : AuditableEntity, IAuditableEntity
{
    private readonly List<ProductOffer> _products = [];

    private Offer()
    {
    }

    private Offer(
        Guid id,
        string code,
        string name,
        DiscountType discountType,
        decimal discountValue,
        DateTime startUtc,
        DateTime endUtc,
        decimal? minimumOrderAmount,
        int? maxRedemptions)
        : base(id)
    {
        Code = code;
        Name = name;
        DiscountType = discountType;
        DiscountValue = discountValue;
        StartUtc = startUtc;
        EndUtc = endUtc;
        MinimumOrderAmount = minimumOrderAmount;
        MaxRedemptions = maxRedemptions;
        IsActive = true;
    }

    /// <summary>Uppercase, unique. What the customer types at checkout.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public DiscountType DiscountType { get; private set; }

    public decimal DiscountValue { get; private set; }

    public DateTime StartUtc { get; private set; }

    public DateTime EndUtc { get; private set; }

    public decimal? MinimumOrderAmount { get; private set; }

    public int? MaxRedemptions { get; private set; }

    public int RedemptionCount { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>An empty set means the offer applies cart-wide (spec §8.2).</summary>
    public IReadOnlyCollection<ProductOffer> Products => _products;

    public static Offer Create(
        string code,
        string name,
        DiscountType discountType,
        decimal discountValue,
        DateTime startUtc,
        DateTime endUtc,
        decimal? minimumOrderAmount = null,
        int? maxRedemptions = null)
    {
        Validate(discountType, discountValue, startUtc, endUtc);

        return new Offer(
            SequentialGuid.New(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            discountType,
            discountValue,
            startUtc,
            endUtc,
            minimumOrderAmount,
            maxRedemptions);
    }

    public void Update(
        string code,
        string name,
        DiscountType discountType,
        decimal discountValue,
        DateTime startUtc,
        DateTime endUtc,
        decimal? minimumOrderAmount,
        int? maxRedemptions,
        bool isActive)
    {
        Validate(discountType, discountValue, startUtc, endUtc);

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        DiscountType = discountType;
        DiscountValue = discountValue;
        StartUtc = startUtc;
        EndUtc = endUtc;
        MinimumOrderAmount = minimumOrderAmount;
        MaxRedemptions = maxRedemptions;
        IsActive = isActive;
    }

    /// <summary>True when the offer may be applied right now.</summary>
    public bool IsRedeemable(DateTime nowUtc) =>
        IsActive
        && nowUtc >= StartUtc
        && nowUtc <= EndUtc
        && (MaxRedemptions is null || RedemptionCount < MaxRedemptions);

    /// <summary>
    /// Computes the discount for a subtotal. Clamped to the subtotal so a fixed-amount
    /// offer larger than the cart cannot produce a negative total.
    /// </summary>
    public decimal CalculateDiscount(decimal subTotal)
    {
        if (MinimumOrderAmount is { } minimum && subTotal < minimum)
        {
            return 0m;
        }

        var discount = DiscountType switch
        {
            DiscountType.Percentage => Math.Round(subTotal * (DiscountValue / 100m), 2, MidpointRounding.AwayFromZero),
            DiscountType.FixedAmount => DiscountValue,
            _ => 0m,
        };

        return Math.Min(discount, subTotal);
    }

    public void Redeem(DateTime nowUtc)
    {
        if (!IsRedeemable(nowUtc))
        {
            throw new DomainRuleException("offer_not_redeemable", $"Offer '{Code}' is not currently redeemable.");
        }

        RedemptionCount++;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void ReplaceProducts(IEnumerable<Guid> productIds)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        var target = productIds.Distinct().ToArray();

        _products.RemoveAll(p => !target.Contains(p.ProductId));

        foreach (var productId in target.Where(id => !_products.Exists(p => p.ProductId == id)))
        {
            _products.Add(new ProductOffer(productId, Id));
        }
    }

    private static void Validate(DiscountType discountType, decimal discountValue, DateTime startUtc, DateTime endUtc)
    {
        if (discountValue <= 0)
        {
            throw new DomainRuleException("discount_value_invalid", "Discount value must be greater than zero.");
        }

        if (discountType == DiscountType.Percentage && discountValue > 100m)
        {
            throw new DomainRuleException("discount_percentage_invalid", "A percentage discount cannot exceed 100.");
        }

        if (endUtc <= startUtc)
        {
            throw new DomainRuleException("offer_window_invalid", "The offer's end date must be after its start date.");
        }
    }
}

/// <summary>Links an offer to a specific product (spec §8.2).</summary>
internal sealed class ProductOffer
{
    private ProductOffer()
    {
    }

    internal ProductOffer(Guid productId, Guid offerId)
    {
        ProductId = productId;
        OfferId = offerId;
    }

    public Guid ProductId { get; private set; }

    public Guid OfferId { get; private set; }
}
