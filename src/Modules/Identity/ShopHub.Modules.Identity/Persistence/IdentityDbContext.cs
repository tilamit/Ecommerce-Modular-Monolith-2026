using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Identity.Domain;

namespace ShopHub.Modules.Identity.Persistence;

/// <summary>
/// The Identity module's data. <c>internal</c> by design (spec §5.1) - no other module can
/// so much as name this type, which is the compiler enforcing the boundary before any
/// architecture test has to.
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>The module's schema, and the migrations history table that goes with it.</summary>
    internal const string Schema = "identity";

    internal const string MigrationsHistoryTable = "__EFMigrationsHistory_identity";

    internal DbSet<User> Users => Set<User>();

    internal DbSet<Role> Roles => Set<Role>();

    internal DbSet<Permission> Permissions => Set<Permission>();

    internal DbSet<MenuItem> MenuItems => Set<MenuItem>();

    internal DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    internal DbSet<UserAddress> UserAddresses => Set<UserAddress>();

    internal DbSet<RoleMenuItem> RoleMenuItems => Set<RoleMenuItem>();

    internal DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    internal DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
