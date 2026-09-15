using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Modules.Catalog.Domain;

namespace ShopHub.Modules.Catalog.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(160).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1024);
        builder.Property(c => c.ImageUrl).HasMaxLength(512);
        builder.Property(c => c.CreatedBy).HasMaxLength(64);
        builder.Property(c => c.ModifiedBy).HasMaxLength(64);
        builder.Property(c => c.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.ModifiedUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.DeletedUtc).HasColumnType("datetime2(3)");

        // Unique among live rows only, so a soft-deleted category does not permanently
        // reserve its slug.
        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Categories_Slug");

        builder.HasIndex(c => new { c.ParentId, c.DisplayOrder })
            .HasDatabaseName("IX_Categories_ParentId_DisplayOrder");

        // Restrict, not cascade: removing a parent must not silently delete its subtree.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(300).IsRequired();
        builder.Property(p => p.ShortDescription).HasMaxLength(512);
        builder.Property(p => p.Description).HasMaxLength(4000);
        builder.Property(p => p.CreatedBy).HasMaxLength(64);
        builder.Property(p => p.ModifiedBy).HasMaxLength(64);

        // Spec A4: decimal(18,2) plus an explicit ISO-4217 code on every monetary column.
        builder.Property(p => p.Price).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.CompareAtPrice).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CurrencyCode).HasColumnType("char(3)").IsRequired();
        builder.Property(p => p.Rating).HasColumnType("decimal(3,2)");

        builder.Property(p => p.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(p => p.ModifiedUtc).HasColumnType("datetime2(3)");
        builder.Property(p => p.DeletedUtc).HasColumnType("datetime2(3)");

        builder.Property(p => p.RowVersion).IsRowVersion();

        // Indexes are designed per query, not per column (spec §6.7, §8.2).
        builder.HasIndex(p => p.Sku)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Products_Sku");

        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Products_Slug");

        builder.HasIndex(p => new { p.CategoryId, p.IsActive })
            .HasDatabaseName("IX_Products_CategoryId_IsActive");

        // Powers "new this month" and the default storefront sort.
        builder.HasIndex(p => new { p.IsActive, p.CreatedUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Products_IsActive_CreatedUtc");

        builder.HasIndex(p => p.Price)
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("IX_Products_Price");

        // Prefix search for typeahead. A leading wildcard cannot use this index, which is
        // exactly why the suggest endpoint uses StartsWith (spec §8.2).
        builder.HasIndex(p => p.Name).HasDatabaseName("IX_Products_Name");

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Images)
            .WithOne()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ProductImages");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Url).HasMaxLength(512).IsRequired();
        builder.Property(i => i.AltText).HasMaxLength(256);

        builder.HasIndex(i => new { i.ProductId, i.DisplayOrder })
            .HasDatabaseName("IX_ProductImages_ProductId_DisplayOrder");
    }
}

internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Offers");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Code).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Name).HasMaxLength(256).IsRequired();
        builder.Property(o => o.DiscountValue).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.MinimumOrderAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.CreatedBy).HasMaxLength(64);
        builder.Property(o => o.ModifiedBy).HasMaxLength(64);

        builder.Property(o => o.StartUtc).HasColumnType("datetime2(3)");
        builder.Property(o => o.EndUtc).HasColumnType("datetime2(3)");
        builder.Property(o => o.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(o => o.ModifiedUtc).HasColumnType("datetime2(3)");

        // Stored as its name, not its ordinal, so inserting an enum member later cannot
        // silently change what existing rows mean.
        builder.Property(o => o.DiscountType).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(o => o.Code).IsUnique().HasDatabaseName("IX_Offers_Code");
        builder.HasIndex(o => new { o.IsActive, o.EndUtc }).HasDatabaseName("IX_Offers_IsActive_EndUtc");

        builder.HasMany(o => o.Products)
            .WithOne()
            .HasForeignKey(p => p.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Products).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductOfferConfiguration : IEntityTypeConfiguration<ProductOffer>
{
    public void Configure(EntityTypeBuilder<ProductOffer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ProductOffers");
        builder.HasKey(p => new { p.ProductId, p.OfferId });

        builder.HasIndex(p => p.OfferId).HasDatabaseName("IX_ProductOffers_OfferId");
    }
}
