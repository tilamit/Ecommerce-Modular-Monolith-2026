namespace ShopHub.Modules.Catalog.Features.Products;

/// <summary>Row in the storefront grid and the admin product list. A projection, never an entity.</summary>
internal sealed record ProductListItem(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? ShortDescription,
    decimal Price,
    decimal? CompareAtPrice,
    string CurrencyCode,
    int StockQuantity,
    bool IsActive,
    bool IsFeatured,
    decimal? Rating,
    int ReviewCount,
    Guid CategoryId,
    string CategoryName,
    string? PrimaryImageUrl,
    DateTime CreatedUtc);

internal sealed record ProductDetail(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? ShortDescription,
    string? Description,
    decimal Price,
    decimal? CompareAtPrice,
    string CurrencyCode,
    int StockQuantity,
    bool IsActive,
    bool IsFeatured,
    decimal? Rating,
    int ReviewCount,
    Guid CategoryId,
    string CategoryName,
    IReadOnlyList<ProductImageResponse> Images,
    DateTime CreatedUtc,
    DateTime? ModifiedUtc);

internal sealed record ProductImageResponse(string Url, string? AltText, int DisplayOrder, bool IsPrimary);

/// <summary>Typeahead result (spec §9.2). Deliberately tiny - this fires on every keystroke.</summary>
internal sealed record ProductSuggestion(Guid Id, string Name, string Slug, decimal Price, string CurrencyCode, string? ImageUrl);

/// <summary>
/// Storefront and admin product filters (spec §9.2).
/// <para>
/// This record is also the cache-key input: it is hashed to build the list cache key, so
/// every field that changes the result set must live here (spec §6.4).
/// </para>
/// </summary>
internal sealed record ProductFilter(
    string? Search,
    IReadOnlyList<Guid>? CategoryIds,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? IsActive,
    bool? IsFeatured,
    bool? InStock,
    DateTime? CreatedFrom,
    DateTime? CreatedTo);

internal sealed record CreateProductRequest(
    Guid CategoryId,
    string Sku,
    string Name,
    string? Slug,
    string? ShortDescription,
    string? Description,
    decimal Price,
    decimal? CompareAtPrice,
    string CurrencyCode,
    int StockQuantity,
    bool IsFeatured,
    IReadOnlyList<ProductImageResponse>? Images);

internal sealed record UpdateProductRequest(
    Guid CategoryId,
    string Sku,
    string Name,
    string? Slug,
    string? ShortDescription,
    string? Description,
    decimal Price,
    decimal? CompareAtPrice,
    string CurrencyCode,
    int StockQuantity,
    bool IsFeatured,
    IReadOnlyList<ProductImageResponse>? Images);

internal sealed record SetStatusRequest(bool IsActive);
