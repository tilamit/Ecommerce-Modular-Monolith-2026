using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Modules.Ordering.Domain;

namespace ShopHub.Modules.Ordering.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Carts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.ModifiedUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.ExpiresUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.LastMergeUtc).HasColumnType("datetime2(3)");

        // Spec §8.3: one active cart per user. A filtered unique index enforces it in the
        // database, so a race between two tabs cannot produce a second active cart.
        builder.HasIndex(c => c.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL AND [Status] = 'Active'")
            .HasDatabaseName("IX_Carts_UserId_Active");

        builder.HasIndex(c => c.AnonymousId).HasDatabaseName("IX_Carts_AnonymousId");

        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CartItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.UnitPriceSnapshot).HasColumnType("decimal(18,2)");
        builder.Property(i => i.AddedUtc).HasColumnType("datetime2(3)");

        // One line per product per cart (spec §8.3); adding the same product again
        // increases the existing line rather than creating a second one.
        builder.HasIndex(i => new { i.CartId, i.ProductId })
            .IsUnique()
            .HasDatabaseName("IX_CartItems_CartId_ProductId");

        // Cross-module reference: indexed but not constrained (spec §4.2).
        builder.HasIndex(i => i.ProductId).HasDatabaseName("IX_CartItems_ProductId");
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Orders", table => table.HasCheckConstraint(
            "CK_Orders_ExactlyOneCustomer",
            // Spec §8.3: exactly one of UserId / GuestCheckoutProfileId is non-null.
            // This is what makes "is this a registered customer?" answerable without
            // ambiguity, and it belongs in the database so no code path can violate it.
            "([UserId] IS NOT NULL AND [GuestCheckoutProfileId] IS NULL) OR " +
            "([UserId] IS NULL AND [GuestCheckoutProfileId] IS NOT NULL)"));

        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.CurrencyCode).HasColumnType("char(3)").IsRequired();
        builder.Property(o => o.AppliedOfferCode).HasMaxLength(64);
        builder.Property(o => o.Notes).HasMaxLength(1024);
        builder.Property(o => o.CreatedBy).HasMaxLength(64);
        builder.Property(o => o.ModifiedBy).HasMaxLength(64);

        foreach (var money in new[] { nameof(Order.SubTotal), nameof(Order.DiscountTotal), nameof(Order.TaxTotal), nameof(Order.ShippingTotal), nameof(Order.GrandTotal) })
        {
            builder.Property(money).HasColumnType("decimal(18,2)");
        }

        builder.Property(o => o.PlacedUtc).HasColumnType("datetime2(3)");
        builder.Property(o => o.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(o => o.ModifiedUtc).HasColumnType("datetime2(3)");

        builder.Property(o => o.RowVersion).IsRowVersion();

        // Snapshot address, owned so it lives in the Orders row itself (spec §8.3).
        builder.OwnsOne(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.FullName).HasColumnName("ShipToFullName").HasMaxLength(256).IsRequired();
            address.Property(a => a.Line1).HasColumnName("ShipToLine1").HasMaxLength(256).IsRequired();
            address.Property(a => a.Line2).HasColumnName("ShipToLine2").HasMaxLength(256);
            address.Property(a => a.City).HasColumnName("ShipToCity").HasMaxLength(128).IsRequired();
            address.Property(a => a.State).HasColumnName("ShipToState").HasMaxLength(128);
            address.Property(a => a.PostalCode).HasColumnName("ShipToPostalCode").HasMaxLength(32).IsRequired();
            address.Property(a => a.Country).HasColumnName("ShipToCountry").HasMaxLength(128).IsRequired();
            address.Property(a => a.PhoneNumber).HasColumnName("ShipToPhoneNumber").HasMaxLength(32);
        });

        builder.HasIndex(o => o.OrderNumber).IsUnique().HasDatabaseName("IX_Orders_OrderNumber");

        builder.HasIndex(o => new { o.UserId, o.PlacedUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Orders_UserId_PlacedUtc");

        builder.HasIndex(o => new { o.Status, o.PlacedUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Orders_Status_PlacedUtc");

        builder.HasIndex(o => o.GuestCheckoutProfileId).HasDatabaseName("IX_Orders_GuestCheckoutProfileId");

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.StatusHistory)
            .WithOne()
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Orders are never deleted (spec A7) - there is no soft-delete flag and no
        // query filter, deliberately.
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OrderItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductNameSnapshot).HasMaxLength(256).IsRequired();
        builder.Property(i => i.SkuSnapshot).HasMaxLength(64).IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(i => i.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.LineTotal).HasColumnType("decimal(18,2)");

        builder.HasIndex(i => i.OrderId).HasDatabaseName("IX_OrderItems_OrderId");
        builder.HasIndex(i => i.ProductId).HasDatabaseName("IX_OrderItems_ProductId");
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OrderStatusHistory");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.ChangedUtc).HasColumnType("datetime2(3)");
        builder.Property(h => h.Reason).HasMaxLength(512);

        builder.HasIndex(h => new { h.OrderId, h.ChangedUtc }).HasDatabaseName("IX_OrderStatusHistory_OrderId_ChangedUtc");
    }
}

internal sealed class GuestCheckoutProfileConfiguration : IEntityTypeConfiguration<GuestCheckoutProfile>
{
    public void Configure(EntityTypeBuilder<GuestCheckoutProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GuestCheckoutProfiles");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Email).HasMaxLength(256).IsRequired();
        builder.Property(g => g.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(g => g.FullName).HasMaxLength(256).IsRequired();
        builder.Property(g => g.PhoneNumber).HasMaxLength(32);
        builder.Property(g => g.Line1).HasMaxLength(256).IsRequired();
        builder.Property(g => g.Line2).HasMaxLength(256);
        builder.Property(g => g.City).HasMaxLength(128).IsRequired();
        builder.Property(g => g.State).HasMaxLength(128);
        builder.Property(g => g.PostalCode).HasMaxLength(32).IsRequired();
        builder.Property(g => g.Country).HasMaxLength(128).IsRequired();
        builder.Property(g => g.PreferredPaymentMethod).HasConversion<string>().HasMaxLength(32);

        builder.Property(g => g.FirstSeenUtc).HasColumnType("datetime2(3)");
        builder.Property(g => g.LastUsedUtc).HasColumnType("datetime2(3)");

        // The upsert key (spec §8.4): one profile per email address.
        builder.HasIndex(g => g.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("IX_GuestCheckoutProfiles_NormalizedEmail");

        builder.HasIndex(g => g.LinkedUserId).HasDatabaseName("IX_GuestCheckoutProfiles_LinkedUserId");
    }
}
