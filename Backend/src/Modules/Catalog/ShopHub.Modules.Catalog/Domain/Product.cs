using ShopHub.Shared.Kernel.Entities;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Catalog.Domain;

/// <summary>
/// A sellable item (spec §8.2). Behaviour lives here - <see cref="AdjustStock"/>,
/// <see cref="Reserve"/>, <see cref="Deactivate"/> - so stock can never go negative
/// regardless of which caller reaches it.
/// </summary>
internal sealed class Product : AuditableEntity, ISoftDeletable, IAuditableEntity
{
    private readonly List<ProductImage> _images = [];

    private Product()
    {
    }

    private Product(
        Guid id,
        Guid categoryId,
        string sku,
        string name,
        string slug,
        decimal price,
        string currencyCode,
        int stockQuantity)
        : base(id)
    {
        CategoryId = categoryId;
        Sku = sku;
        Name = name;
        Slug = slug;
        Price = price;
        CurrencyCode = currencyCode;
        StockQuantity = stockQuantity;
        IsActive = true;
    }

    public Guid CategoryId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? ShortDescription { get; private set; }

    public string? Description { get; private set; }

    /// <summary>decimal(18,2) - never a float (spec A4).</summary>
    public decimal Price { get; private set; }

    /// <summary>Optional "was" price, for showing a discount. Must exceed <see cref="Price"/> to mean anything.</summary>
    public decimal? CompareAtPrice { get; private set; }

    /// <summary>ISO-4217, on every monetary row (spec A4).</summary>
    public string CurrencyCode { get; private set; } = "USD";

    public int StockQuantity { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsFeatured { get; private set; }

    public decimal? Rating { get; private set; }

    public int ReviewCount { get; private set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    /// <summary>Optimistic concurrency token (spec §6.7). Two admins editing at once get a 409, not a lost update.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<ProductImage> Images => _images;

    public bool IsInStock => StockQuantity > 0;

    public static Product Create(
        Guid categoryId,
        string sku,
        string name,
        string slug,
        decimal price,
        int stockQuantity,
        string currencyCode = "USD") =>
        new(
            SequentialGuid.New(),
            categoryId,
            sku.Trim().ToUpperInvariant(),
            name.Trim(),
            Category.Slugify(slug),
            price,
            currencyCode.ToUpperInvariant(),
            stockQuantity);

    public void Update(
        Guid categoryId,
        string sku,
        string name,
        string slug,
        string? shortDescription,
        string? description,
        decimal price,
        decimal? compareAtPrice,
        string currencyCode,
        int stockQuantity,
        bool isFeatured)
    {
        if (compareAtPrice is { } compare && compare <= price)
        {
            throw new DomainRuleException(
                "compare_at_price_invalid",
                "CompareAtPrice must be greater than Price, or omitted.");
        }

        CategoryId = categoryId;
        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        Slug = Category.Slugify(slug);
        ShortDescription = shortDescription?.Trim();
        Description = description?.Trim();
        Price = price;
        CompareAtPrice = compareAtPrice;
        CurrencyCode = currencyCode.ToUpperInvariant();
        StockQuantity = stockQuantity;
        IsFeatured = isFeatured;
    }

    public void Rename(string name, string slug)
    {
        Name = name.Trim();
        Slug = Category.Slugify(slug);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SetRating(decimal? rating, int reviewCount)
    {
        Rating = rating;
        ReviewCount = reviewCount;
    }

    /// <summary>Absolute stock correction, for admin edits and stock takes.</summary>
    public void AdjustStock(int quantity)
    {
        if (quantity < 0)
        {
            throw new DomainRuleException("stock_negative", "Stock quantity cannot be negative.");
        }

        StockQuantity = quantity;
    }

    /// <summary>
    /// Decrements stock at order placement (spec §8.4). Refuses rather than clamping: a
    /// silent partial reservation would let an order ship fewer items than were paid for.
    /// </summary>
    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainRuleException("quantity_invalid", "Reserved quantity must be greater than zero.");
        }

        if (!IsActive || IsDeleted)
        {
            throw new DomainRuleException("product_unavailable", $"'{Name}' is no longer available.");
        }

        if (StockQuantity < quantity)
        {
            throw new DomainRuleException(
                "insufficient_stock",
                $"'{Name}' has {StockQuantity} in stock but {quantity} were requested.");
        }

        StockQuantity -= quantity;
    }

    /// <summary>Returns stock to the shelf when an order is cancelled (ADR-003).</summary>
    public void Release(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainRuleException("quantity_invalid", "Released quantity must be greater than zero.");
        }

        StockQuantity += quantity;
    }

    public void ReplaceImages(IEnumerable<(string Url, string? AltText, int DisplayOrder, bool IsPrimary)> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        _images.Clear();

        foreach (var (url, altText, displayOrder, isPrimary) in images)
        {
            _images.Add(ProductImage.Create(Id, url, altText, displayOrder, isPrimary));
        }
    }
}

/// <summary>An image attached to a product (spec §8.2).</summary>
internal sealed class ProductImage : Entity
{
    private ProductImage()
    {
    }

    private ProductImage(Guid id, Guid productId, string url, string? altText, int displayOrder, bool isPrimary)
        : base(id)
    {
        ProductId = productId;
        Url = url;
        AltText = altText;
        DisplayOrder = displayOrder;
        IsPrimary = isPrimary;
    }

    public Guid ProductId { get; private set; }

    public string Url { get; private set; } = string.Empty;

    public string? AltText { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsPrimary { get; private set; }

    internal static ProductImage Create(Guid productId, string url, string? altText, int displayOrder, bool isPrimary) =>
        new(SequentialGuid.New(), productId, url.Trim(), altText?.Trim(), displayOrder, isPrimary);
}
