using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Modules.Identity.Domain;

namespace ShopHub.Modules.Identity.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(32);
        builder.Property(u => u.CreatedBy).HasMaxLength(64);
        builder.Property(u => u.ModifiedBy).HasMaxLength(64);

        // Spec A6: UTC, datetime2(3). Names already end in Utc.
        builder.Property(u => u.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(u => u.ModifiedUtc).HasColumnType("datetime2(3)");
        builder.Property(u => u.DeletedUtc).HasColumnType("datetime2(3)");
        builder.Property(u => u.LastLoginUtc).HasColumnType("datetime2(3)");
        builder.Property(u => u.LockoutEndUtc).HasColumnType("datetime2(3)");

        builder.Property(u => u.RowVersion).IsRowVersion();

        // Unique email among live rows only, so a soft-deleted account does not block
        // re-registration of the same address (spec §8.1).
        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Users_NormalizedEmail_Active");

        builder.HasIndex(u => new { u.IsActive, u.CreatedUtc })
            .HasDatabaseName("IX_Users_IsActive_CreatedUtc");

        builder.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.Addresses)
            .WithOne()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(u => u.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Spec A7: soft delete via a named query filter, so every read excludes deleted
        // rows unless a caller explicitly opts out with IgnoreQueryFilters().
        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserRoles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.HasOne(ur => ur.Role)
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ur => ur.RoleId).HasDatabaseName("IX_UserRoles_RoleId");
    }
}

internal sealed class UserAddressConfiguration : IEntityTypeConfiguration<UserAddress>
{
    public void Configure(EntityTypeBuilder<UserAddress> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserAddresses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Label).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Line1).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Line2).HasMaxLength(256);
        builder.Property(a => a.City).HasMaxLength(128).IsRequired();
        builder.Property(a => a.State).HasMaxLength(128);
        builder.Property(a => a.PostalCode).HasMaxLength(32).IsRequired();
        builder.Property(a => a.Country).HasMaxLength(128).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(64);
        builder.Property(a => a.ModifiedBy).HasMaxLength(64);
        builder.Property(a => a.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(a => a.ModifiedUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(a => a.UserId).HasDatabaseName("IX_UserAddresses_UserId");
    }
}
