using Microsoft.EntityFrameworkCore;
using ShopHub.Shared.Infrastructure.Persistence;
using ShopHub.Modules.Catalog.Domain;

namespace ShopHub.Modules.Catalog.Persistence;

/// <summary>
/// The Catalog module's data. <c>internal</c> by design (spec §5.1) - Ordering cannot name
/// this type, let alone join to it, which is what forces stock and pricing to go through
/// <c>ICatalogModuleApi</c>.
/// </summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    internal const string Schema = "catalog";

    internal const string MigrationsHistoryTable = "__EFMigrationsHistory_catalog";

    internal DbSet<Category> Categories => Set<Category>();

    internal DbSet<Product> Products => Set<Product>();

    internal DbSet<ProductImage> ProductImages => Set<ProductImage>();

    internal DbSet<Offer> Offers => Set<Offer>();

    internal DbSet<ProductOffer> ProductOffers => Set<ProductOffer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        // Spec A5: the application generates v7 GUIDs, so EF must not infer entity state
        // from the key value (see ModelBuilderExtensions for the failure mode this avoids).
        modelBuilder.UseApplicationGeneratedGuidKeys();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
