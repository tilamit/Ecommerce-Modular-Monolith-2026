using Microsoft.EntityFrameworkCore;
using ShopHub.Shared.Infrastructure.Modules;

namespace ShopHub.Modules.Identity.Persistence;

/// <summary>
/// Applies the Identity module's migrations and runs its seeder (spec §13).
/// <para>
/// Migrations are per module with their own history table, so this affects only the
/// <c>identity</c> schema - another module's pending migration cannot block it and it
/// cannot touch another module's tables.
/// </para>
/// </summary>
internal sealed class IdentityInitializer(IdentityDbContext db, IdentitySeeder seeder) : IModuleInitializer
{
    public string ModuleName => "identity";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await seeder.SeedAsync(cancellationToken);
    }
}
