using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Catalog.Domain;
using ShopHub.Modules.Catalog.Infrastructure;
using ShopHub.Modules.Catalog.Persistence;
using ShopHub.Shared.Infrastructure.Caching;
using ShopHub.Shared.Infrastructure.Persistence;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Catalog.Features.Products;

/// <summary>Product browse, search and administration (spec §9.2).</summary>
internal sealed class ProductService(
    CatalogDbContext db,
    HybridCache cache,
    ICatalogCacheInvalidator invalidator,
    IClock clock,
    IAuditWriter audit)
{
    /// <summary>Typeahead returns at most this many rows (spec §9.2).</summary>
    private const int SuggestionLimit = 10;

    /// <summary>"New arrivals" window (spec §9.2).</summary>
    private const int NewArrivalDays = 30;

    private static readonly HybridCacheEntryOptions ListCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(2),
        LocalCacheExpiration = TimeSpan.FromMinutes(2),
    };

    private static readonly HybridCacheEntryOptions DetailCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// Sortable columns, whitelisted (spec §6.5). A client string never reaches
    /// <c>OrderBy</c> and an unknown value falls back to the default rather than erroring.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<Product, object?>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["price"] = p => p.Price,
            ["name"] = p => p.Name,
            ["rating"] = p => p.Rating,
            ["createdUtc"] = p => p.CreatedUtc,
            ["stockQuantity"] = p => p.StockQuantity,
        };

    /// <summary>
    /// Paged product list. Cached for anonymous callers only.
    /// <para>
    /// The cache key is the module, the entity and a hash of the filter plus paging
    /// (spec §6.4) - never raw user input, which would let a caller mint unbounded keys.
    /// </para>
    /// </summary>
    internal async Task<PagedResult<ProductListItem>> GetProductsAsync(
        PagedRequest paging,
        ProductFilter filter,
        bool useCache,
        CancellationToken cancellationToken)
    {
        var request = paging.Normalized();

        if (!useCache)
        {
            return await QueryProductsAsync(request, filter);
        }

        var key = CacheKeys.For(
            "catalog",
            "products:page",
            CacheKeys.HashFilter(new { request.Page, request.PageSize, request.Sort, filter }));

        return await cache.GetOrCreateSharedAsync(
            key,
            (Service: this, Request: request, Filter: filter),
            static async (state, _) => await state.Service.QueryProductsAsync(state.Request, state.Filter),
            ListCacheOptions,
            tags: [CacheTags.Products]);
    }

    internal async Task<ProductDetail> GetProductAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var key = CacheKeys.For("catalog", "product", idOrSlug.ToLowerInvariant());

        var product = await cache.GetOrCreateSharedAsync(
            key,
            (Service: this, IdOrSlug: idOrSlug),
            static async (state, ct) => await state.Service.QueryProductAsync(state.IdOrSlug, ct),
            DetailCacheOptions,
            tags: [CacheTags.Products]);

        return product ?? throw NotFoundException.For("Product", idOrSlug);
    }

    /// <summary>
    /// Typeahead (spec §9.2, §8.2).
    /// <para>
    /// Prefix match only. A leading wildcard (<c>LIKE '%term%'</c>) cannot use the
    /// <c>IX_Products_Name</c> index and is the first thing to fall over as the catalogue
    /// grows - the spec calls this out explicitly and full-text search is the documented
    /// upgrade path if mid-word matching is ever required.
    /// </para>
    /// </summary>
    internal async Task<IReadOnlyList<ProductSuggestion>> SuggestAsync(string? term, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var prefix = term.Trim();

        return await db.Products
            .Where(p => p.IsActive && p.Name.StartsWith(prefix))
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Take(SuggestionLimit)
            .Select(p => new ProductSuggestion(
                p.Id,
                p.Name,
                p.Slug,
                p.Price,
                p.CurrencyCode,
                p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Products added in the last 30 days (spec §9.2).</summary>
    internal async Task<PagedResult<ProductListItem>> GetNewArrivalsAsync(
        PagedRequest paging,
        CancellationToken cancellationToken)
    {
        var since = clock.UtcNow.AddDays(-NewArrivalDays);

        var filter = new ProductFilter(null, null, null, null, IsActive: true, null, null, CreatedFrom: since, null);
        var request = (paging with { Sort = "-createdUtc" }).Normalized();

        return await GetProductsAsync(request, filter, useCache: true, cancellationToken);
    }

    internal async Task<ProductDetail> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

        var slug = Category.Slugify(request.Slug ?? request.Name);
        await EnsureUniqueAsync(request.Sku, slug, excludingId: null, cancellationToken);

        var product = Product.Create(
            request.CategoryId,
            request.Sku,
            request.Name,
            slug,
            request.Price,
            request.StockQuantity,
            request.CurrencyCode);

        product.Update(
            request.CategoryId,
            request.Sku,
            request.Name,
            slug,
            request.ShortDescription,
            request.Description,
            request.Price,
            request.CompareAtPrice,
            request.CurrencyCode,
            request.StockQuantity,
            request.IsFeatured);

        ApplyImages(product, request.Images);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateProductsAsync(cancellationToken);

        WriteImagesChange(product, before: [], after: DescribeImages(product.Images));

        return await QueryProductAsync(product.Id.ToString(), cancellationToken)
            ?? throw NotFoundException.For("Product", product.Id);
    }

    internal async Task<ProductDetail> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await TrackedProductAsync(id, cancellationToken);

        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

        var slug = Category.Slugify(request.Slug ?? request.Name);
        await EnsureUniqueAsync(request.Sku, slug, excludingId: id, cancellationToken);

        product.Update(
            request.CategoryId,
            request.Sku,
            request.Name,
            slug,
            request.ShortDescription,
            request.Description,
            request.Price,
            request.CompareAtPrice,
            request.CurrencyCode,
            request.StockQuantity,
            request.IsFeatured);

        var imagesBefore = DescribeImages(product.Images);

        ApplyImages(product, request.Images);

        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateProductsAsync(cancellationToken);

        WriteImagesChange(product, imagesBefore, DescribeImages(product.Images));

        return await QueryProductAsync(id.ToString(), cancellationToken) ?? throw NotFoundException.For("Product", id);
    }

    internal async Task SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var product = await TrackedProductAsync(id, cancellationToken);

        if (isActive)
        {
            product.Activate();
        }
        else
        {
            product.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateProductsAsync(cancellationToken);
    }

    /// <summary>Soft delete (spec A7) - the interceptor turns the remove into a flag update.</summary>
    internal async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await TrackedProductAsync(id, cancellationToken);

        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateProductsAsync(cancellationToken);
    }

    // --- queries ---------------------------------------------------------

    /// <summary>
    /// The uncached query. Kept separate so the cached and uncached paths cannot drift:
    /// both the admin (uncached) and storefront (cached) callers run exactly this.
    /// <para>
    /// Takes no cancellation token on purpose. See <see cref="ReadCancellation"/>: a bounded
    /// paged read is not worth abandoning and threading the caller's token here is what
    /// made an ordinary signed-in page load raise <c>OperationCanceledException</c>.
    /// </para>
    /// </summary>
    private async Task<PagedResult<ProductListItem>> QueryProductsAsync(
        PagedRequest request,
        ProductFilter filter)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(ReadCancellation.RunToCompletion);

        var items = await Sort(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(p => new ProductListItem(
                p.Id,
                p.Sku,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.Price,
                p.CompareAtPrice,
                p.CurrencyCode,
                p.StockQuantity,
                p.IsActive,
                p.IsFeatured,
                p.Rating,
                p.ReviewCount,
                p.CategoryId,
                db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
                p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault(),
                p.CreatedUtc))
            .ToListAsync(ReadCancellation.RunToCompletion);

        return new PagedResult<ProductListItem>(items, request.Page, request.PageSize, total);
    }

    private async Task<ProductDetail?> QueryProductAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var query = Guid.TryParse(idOrSlug, out var id)
            ? db.Products.Where(p => p.Id == id)
            : db.Products.Where(p => p.Slug == idOrSlug);

        return await query
            .Select(p => new ProductDetail(
                p.Id,
                p.Sku,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.Description,
                p.Price,
                p.CompareAtPrice,
                p.CurrencyCode,
                p.StockQuantity,
                p.IsActive,
                p.IsFeatured,
                p.Rating,
                p.ReviewCount,
                p.CategoryId,
                db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
                p.Images
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => new ProductImageResponse(i.Url, i.AltText, i.DisplayOrder, i.IsPrimary))
                    .ToList(),
                p.CreatedUtc,
                p.ModifiedUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private IQueryable<Product> Filtered(ProductFilter filter)
    {
        var query = db.Products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();

            // Prefix match, for the same index reason as the typeahead above.
            query = query.Where(p => p.Name.StartsWith(term) || p.Sku.StartsWith(term));
        }

        if (filter.CategoryIds is { Count: > 0 } categoryIds)
        {
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        }

        if (filter.MinPrice is { } minPrice)
        {
            query = query.Where(p => p.Price >= minPrice);
        }

        if (filter.MaxPrice is { } maxPrice)
        {
            query = query.Where(p => p.Price <= maxPrice);
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(p => p.IsActive == isActive);
        }

        if (filter.IsFeatured is { } isFeatured)
        {
            query = query.Where(p => p.IsFeatured == isFeatured);
        }

        if (filter.InStock is { } inStock)
        {
            query = inStock ? query.Where(p => p.StockQuantity > 0) : query.Where(p => p.StockQuantity == 0);
        }

        if (filter.CreatedFrom is { } from)
        {
            query = query.Where(p => p.CreatedUtc >= from);
        }

        if (filter.CreatedTo is { } to)
        {
            query = query.Where(p => p.CreatedUtc <= to);
        }

        return query;
    }

    /// <summary>
    /// Whitelisted sort with a deterministic tiebreaker. Without the tiebreaker, rows with
    /// equal sort keys can swap between pages, so paging skips one and repeats another
    /// (spec §6.5).
    /// </summary>
    private static IQueryable<Product> Sort(IQueryable<Product> query, PagedRequest request)
    {
        var field = request.SortField;

        if (field is null || !Sortable.TryGetValue(field, out var selector))
        {
            return query.OrderByDescending(p => p.CreatedUtc).ThenBy(p => p.Id);
        }

        return request.SortDescending
            ? query.OrderByDescending(selector).ThenBy(p => p.Id)
            : query.OrderBy(selector).ThenBy(p => p.Id);
    }

    private async Task<Product> TrackedProductAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Products
            .AsTracking()
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw NotFoundException.For("Product", id);

    private async Task EnsureCategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new NotFoundException("category_not_found", $"Category '{categoryId}' was not found.");
        }
    }

    private async Task EnsureUniqueAsync(string sku, string slug, Guid? excludingId, CancellationToken cancellationToken)
    {
        var normalizedSku = sku.Trim().ToUpperInvariant();

        var clash = await db.Products
            .Where(p => excludingId == null || p.Id != excludingId)
            .Where(p => p.Sku == normalizedSku || p.Slug == slug)
            .Select(p => new { p.Sku, p.Slug })
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is null)
        {
            return;
        }

        throw string.Equals(clash.Sku, normalizedSku, StringComparison.Ordinal)
            ? new ConflictException("sku_taken", $"A product with SKU '{normalizedSku}' already exists.")
            : new ConflictException("slug_taken", $"A product with slug '{slug}' already exists.");
    }

    /// <summary>
    /// Images are child rows that are replaced as a set on every save, so the change-tracking
    /// capture would record every image as deleted and re-added even when nothing changed.
    /// The product's image list is compared instead and written as one entry when it differs.
    /// </summary>
    private void WriteImagesChange(Product product, IReadOnlyList<string> before, IReadOnlyList<string> after) =>
        audit.WriteListChange(
            AuditAction.Update,
            CatalogDbContext.Schema,
            nameof(Product),
            product.Id,
            "Product",
            product.Name,
            "Images",
            before,
            after);

    private static List<string> DescribeImages(IEnumerable<ProductImage> images) =>
        [.. images
            .OrderBy(i => i.DisplayOrder)
            .Select(i => i.IsPrimary ? $"{i.Url} (primary)" : i.Url)];

    private static void ApplyImages(Product product, IReadOnlyList<ProductImageResponse>? images)
    {
        if (images is null)
        {
            return;
        }

        product.ReplaceImages(images.Select(i => (i.Url, i.AltText, i.DisplayOrder, i.IsPrimary)));
    }
}
