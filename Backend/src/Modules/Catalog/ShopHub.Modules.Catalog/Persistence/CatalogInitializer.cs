using Microsoft.EntityFrameworkCore;
using ShopHub.Shared.Infrastructure.Modules;

namespace ShopHub.Modules.Catalog.Persistence;

/// <summary>
/// Applies the Catalog module's migrations and runs its seeder (spec §13).
/// Scoped to the <c>catalog</c> schema and its own history table, so it neither blocks nor
/// is blocked by another module's migrations.
/// </summary>
internal sealed class CatalogInitializer(CatalogDbContext db, CatalogSeeder seeder) : IModuleInitializer
{
    public string ModuleName => "catalog";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await seeder.SeedAsync(cancellationToken);
    }
}
