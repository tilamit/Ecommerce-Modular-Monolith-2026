using ShopHub.Shared.Kernel.Entities;

namespace ShopHub.Modules.Catalog.Domain;

/// <summary>
/// A node in the category tree (spec §8.2). Self-referencing, so subcategories are a
/// parent id rather than a second table.
/// </summary>
internal sealed class Category : AuditableEntity, ISoftDeletable, IAuditableEntity
{
    private Category()
    {
    }

    private Category(Guid id, string name, string slug, Guid? parentId, string? description, string? imageUrl, int displayOrder)
        : base(id)
    {
        Name = name;
        Slug = slug;
        ParentId = parentId;
        Description = description;
        ImageUrl = imageUrl;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public Guid? ParentId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>URL-safe identifier, unique. The storefront routes on this, not on the id.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? ImageUrl { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    public static Category Create(
        string name,
        string slug,
        Guid? parentId = null,
        string? description = null,
        string? imageUrl = null,
        int displayOrder = 0) =>
        new(SequentialGuid.New(), name.Trim(), Slugify(slug), parentId, description?.Trim(), imageUrl?.Trim(), displayOrder);

    public void Update(string name, string slug, Guid? parentId, string? description, string? imageUrl, int displayOrder)
    {
        Name = name.Trim();
        Slug = Slugify(slug);
        ParentId = parentId;
        Description = description?.Trim();
        ImageUrl = imageUrl?.Trim();
        DisplayOrder = displayOrder;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>
    /// Normalizes a slug to lowercase with single hyphens. Applied on write so the unique
    /// index cannot be defeated by casing and so a URL is never case-sensitive.
    /// </summary>
    internal static string Slugify(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(trimmed.Length);
        var lastWasHyphen = false;

        foreach (var character in trimmed)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
