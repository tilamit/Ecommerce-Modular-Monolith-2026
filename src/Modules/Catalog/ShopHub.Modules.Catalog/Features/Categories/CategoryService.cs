using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using ShopHub.Modules.Catalog.Domain;
using ShopHub.Modules.Catalog.Infrastructure;
using ShopHub.Modules.Catalog.Persistence;
using ShopHub.Shared.Infrastructure.Caching;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Catalog.Features.Categories;

internal sealed record CategoryNode(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive,
    int ProductCount,
    IReadOnlyList<CategoryNode> Children);

internal sealed record CategoryListItem(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive,
    int ProductCount);

internal sealed record CreateCategoryRequest(
    string Name,
    string? Slug,
    Guid? ParentId,
    string? Description,
    string? ImageUrl,
    int DisplayOrder);

internal sealed record UpdateCategoryRequest(
    string Name,
    string? Slug,
    Guid? ParentId,
    string? Description,
    string? ImageUrl,
    int DisplayOrder);

/// <summary>Category tree and administration (spec §9.2).</summary>
internal sealed class CategoryService(CatalogDbContext db, HybridCache cache, ICatalogCacheInvalidator invalidator)
{
    /// <summary>The tree changes rarely, so it earns a long TTL (spec §6.4).</summary>
    private static readonly HybridCacheEntryOptions TreeCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(30),
    };

    /// <summary>The active tree, for the storefront. Cached 30 minutes, invalidated on any category write.</summary>
    internal async Task<IReadOnlyList<CategoryNode>> GetTreeAsync(CancellationToken cancellationToken)
    {
        var flat = await cache.GetOrCreateAsync(
            CacheKeys.For("catalog", "categories", "tree:active"),
            this,
            static async (service, ct) => await service.LoadFlatAsync(activeOnly: true, ct),
            TreeCacheOptions,
            tags: [CacheTags.Categories],
            cancellationToken: cancellationToken);

        return BuildTree(flat, parentId: null);
    }

    /// <summary>
    /// Paged flat list for the admin grid, including inactive rows.
    /// <para>
    /// Paged even though a catalogue has few categories - spec §6.5 admits no exceptions,
    /// "including admin endpoints, including dropdowns".
    /// </para>
    /// </summary>
    internal async Task<PagedResult<CategoryListItem>> GetCategoriesAsync(
        PagedRequest paging,
        CancellationToken cancellationToken)
    {
        var request = paging.Normalized();
        var query = db.Categories.AsQueryable();

        if (request.Search is { } search)
        {
            query = query.Where(c => c.Name.StartsWith(search));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(c => new CategoryListItem(
                c.Id,
                c.ParentId,
                c.Name,
                c.Slug,
                c.Description,
                c.ImageUrl,
                c.DisplayOrder,
                c.IsActive,
                db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);

        return new PagedResult<CategoryListItem>(items, request.Page, request.PageSize, total);
    }

    internal async Task<CategoryListItem> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var slug = Category.Slugify(request.Slug ?? request.Name);

        await EnsureSlugFreeAsync(slug, excludingId: null, cancellationToken);
        await EnsureParentExistsAsync(request.ParentId, cancellationToken);

        var category = Category.Create(
            request.Name,
            slug,
            request.ParentId,
            request.Description,
            request.ImageUrl,
            request.DisplayOrder);

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateCategoriesAsync(cancellationToken);

        return await GetOneAsync(category.Id, cancellationToken);
    }

    internal async Task<CategoryListItem> UpdateAsync(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await TrackedAsync(id, cancellationToken);
        var slug = Category.Slugify(request.Slug ?? request.Name);

        await EnsureSlugFreeAsync(slug, excludingId: id, cancellationToken);
        await EnsureParentExistsAsync(request.ParentId, cancellationToken);
        await EnsureNoCycleAsync(id, request.ParentId, cancellationToken);

        category.Update(
            request.Name,
            slug,
            request.ParentId,
            request.Description,
            request.ImageUrl,
            request.DisplayOrder);

        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateCategoriesAsync(cancellationToken);

        return await GetOneAsync(id, cancellationToken);
    }

    internal async Task SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var category = await TrackedAsync(id, cancellationToken);

        category.SetActive(isActive);
        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateCategoriesAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes a category (spec §9.2).
    /// <para>
    /// Refuses with 409 if products still reference it, unless <paramref name="reassignTo"/>
    /// names a destination. Without the guard, deleting a category would orphan its products
    /// behind a foreign key that no longer resolves.
    /// </para>
    /// </summary>
    internal async Task DeleteAsync(Guid id, Guid? reassignTo, CancellationToken cancellationToken)
    {
        var category = await TrackedAsync(id, cancellationToken);

        var childCount = await db.Categories.CountAsync(c => c.ParentId == id, cancellationToken);

        if (childCount > 0)
        {
            throw new ConflictException(
                "category_has_children",
                $"This category has {childCount} subcategor{(childCount == 1 ? "y" : "ies")}. Move or delete them first.");
        }

        var productCount = await db.Products.CountAsync(p => p.CategoryId == id, cancellationToken);

        if (productCount > 0)
        {
            if (reassignTo is not { } destination)
            {
                throw new ConflictException(
                    "category_has_products",
                    $"This category has {productCount} product(s). Supply ?reassignTo={{categoryId}} to move them.");
            }

            if (destination == id)
            {
                throw new ConflictException("reassign_to_self", "Products cannot be reassigned to the category being deleted.");
            }

            if (!await db.Categories.AnyAsync(c => c.Id == destination, cancellationToken))
            {
                throw new NotFoundException("category_not_found", $"Category '{destination}' was not found.");
            }

            // Bulk update: this bypasses the change tracker and therefore the audit
            // interceptor (spec §6.7), which is why the reassignment is logged explicitly
            // by the audit entry the endpoint writes in Phase 5.
            await db.Products
                .Where(p => p.CategoryId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.CategoryId, destination), cancellationToken);
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateCategoriesAsync(cancellationToken);
    }

    // --- helpers ---------------------------------------------------------

    private async Task<FlatCategory[]> LoadFlatAsync(bool activeOnly, CancellationToken cancellationToken) =>
        await db.Categories
            .Where(c => !activeOnly || c.IsActive)
            .Select(c => new FlatCategory(
                c.Id,
                c.ParentId,
                c.Name,
                c.Slug,
                c.Description,
                c.ImageUrl,
                c.DisplayOrder,
                c.IsActive,
                db.Products.Count(p => p.CategoryId == c.Id && p.IsActive)))
            .ToArrayAsync(cancellationToken);

    /// <summary>
    /// Assembles rows into a tree in memory. The set is small and bounded, so a recursive
    /// CTE would add SQL complexity for no measurable gain.
    /// </summary>
    private static List<CategoryNode> BuildTree(IReadOnlyCollection<FlatCategory> nodes, Guid? parentId) =>
        [.. nodes
            .Where(n => n.ParentId == parentId)
            .OrderBy(n => n.DisplayOrder)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .Select(n => new CategoryNode(
                n.Id,
                n.ParentId,
                n.Name,
                n.Slug,
                n.Description,
                n.ImageUrl,
                n.DisplayOrder,
                n.IsActive,
                n.ProductCount,
                BuildTree(nodes, n.Id)))];

    private async Task<CategoryListItem> GetOneAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Categories
            .Where(c => c.Id == id)
            .Select(c => new CategoryListItem(
                c.Id,
                c.ParentId,
                c.Name,
                c.Slug,
                c.Description,
                c.ImageUrl,
                c.DisplayOrder,
                c.IsActive,
                db.Products.Count(p => p.CategoryId == c.Id)))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw NotFoundException.For("Category", id);

    private async Task<Category> TrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw NotFoundException.For("Category", id);

    private async Task EnsureSlugFreeAsync(string slug, Guid? excludingId, CancellationToken cancellationToken)
    {
        var taken = await db.Categories
            .AnyAsync(c => c.Slug == slug && (excludingId == null || c.Id != excludingId), cancellationToken);

        if (taken)
        {
            throw new ConflictException("slug_taken", $"A category with slug '{slug}' already exists.");
        }
    }

    private async Task EnsureParentExistsAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is not { } id)
        {
            return;
        }

        if (!await db.Categories.AnyAsync(c => c.Id == id, cancellationToken))
        {
            throw new NotFoundException("category_not_found", $"Parent category '{id}' was not found.");
        }
    }

    /// <summary>
    /// Walks up the proposed parent chain. Without this, setting a category's parent to its
    /// own descendant would create a cycle, and the recursive tree build would never
    /// terminate.
    /// </summary>
    private async Task EnsureNoCycleAsync(Guid id, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return;
        }

        if (parentId == id)
        {
            throw new DomainRuleException("category_cycle", "A category cannot be its own parent.");
        }

        var parents = await db.Categories
            .Select(c => new { c.Id, c.ParentId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentId, cancellationToken);

        var cursor = parentId;
        var guard = 0;

        while (cursor is { } current && guard++ < parents.Count + 1)
        {
            if (current == id)
            {
                throw new DomainRuleException(
                    "category_cycle",
                    "That parent is a descendant of this category, which would create a cycle.");
            }

            cursor = parents.TryGetValue(current, out var next) ? next : null;
        }
    }

    /// <summary>Cache-friendly row shape: a record, not an entity (spec §6.4).</summary>
    private sealed record FlatCategory(
        Guid Id,
        Guid? ParentId,
        string Name,
        string Slug,
        string? Description,
        string? ImageUrl,
        int DisplayOrder,
        bool IsActive,
        int ProductCount);
}

internal sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Slug).MaximumLength(160);
        RuleFor(r => r.Description).MaximumLength(1024);
        RuleFor(r => r.ImageUrl).MaximumLength(512);
    }
}

internal sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Slug).MaximumLength(160);
        RuleFor(r => r.Description).MaximumLength(1024);
        RuleFor(r => r.ImageUrl).MaximumLength(512);
    }
}
