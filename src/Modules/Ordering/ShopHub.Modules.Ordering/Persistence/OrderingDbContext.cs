using Microsoft.EntityFrameworkCore;
using ShopHub.Shared.Infrastructure.Persistence;
using ShopHub.Modules.Ordering.Domain;

namespace ShopHub.Modules.Ordering.Persistence;

/// <summary>
/// The Ordering module's data. <c>internal</c> by design (spec §5.1).
/// <para>
/// Contains no navigation to <c>catalog</c> or <c>identity</c>: product and customer facts
/// arrive as snapshots or through Contracts calls, never as a join.
/// </para>
/// </summary>
internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    internal const string Schema = "ordering";

    internal const string MigrationsHistoryTable = "__EFMigrationsHistory_ordering";

    internal DbSet<Cart> Carts => Set<Cart>();

    internal DbSet<CartItem> CartItems => Set<CartItem>();

    internal DbSet<Order> Orders => Set<Order>();

    internal DbSet<OrderItem> OrderItems => Set<OrderItem>();

    internal DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    internal DbSet<GuestCheckoutProfile> GuestCheckoutProfiles => Set<GuestCheckoutProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        // Spec A5: the application generates v7 GUIDs, so EF must not infer entity state
        // from the key value (see ModelBuilderExtensions for the failure mode this avoids).
        modelBuilder.UseApplicationGeneratedGuidKeys();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
