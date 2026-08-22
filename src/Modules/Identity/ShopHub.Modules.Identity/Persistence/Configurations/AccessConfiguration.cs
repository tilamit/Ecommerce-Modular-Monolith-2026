using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Modules.Identity.Domain;

namespace ShopHub.Modules.Identity.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(64).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(256);
        builder.Property(r => r.CreatedBy).HasMaxLength(64);
        builder.Property(r => r.ModifiedBy).HasMaxLength(64);
        builder.Property(r => r.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(r => r.ModifiedUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(r => r.NormalizedName).IsUnique().HasDatabaseName("IX_Roles_NormalizedName");

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.MenuItems)
            .WithOne()
            .HasForeignKey(rm => rm.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(r => r.MenuItems).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Permissions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(128).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Group).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(256);

        builder.HasIndex(p => p.Code).IsUnique().HasDatabaseName("IX_Permissions_Code");
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RolePermissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });

        builder.HasOne(rp => rp.Permission)
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rp => rp.PermissionId).HasDatabaseName("IX_RolePermissions_PermissionId");
    }
}

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MenuItems");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title).HasMaxLength(128).IsRequired();
        builder.Property(m => m.Icon).HasMaxLength(64);
        builder.Property(m => m.Route).HasMaxLength(256);
        builder.Property(m => m.CreatedBy).HasMaxLength(64);
        builder.Property(m => m.ModifiedBy).HasMaxLength(64);
        builder.Property(m => m.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(m => m.ModifiedUtc).HasColumnType("datetime2(3)");

        // Self-reference for nested menus. Restrict rather than cascade: deleting a parent
        // should be a deliberate act, not a silent subtree removal.
        builder.HasOne<MenuItem>()
            .WithMany()
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.RequiredPermission)
            .WithMany()
            .HasForeignKey(m => m.RequiredPermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ParentId, m.DisplayOrder }).HasDatabaseName("IX_MenuItems_ParentId_DisplayOrder");
    }
}

internal sealed class RoleMenuItemConfiguration : IEntityTypeConfiguration<RoleMenuItem>
{
    public void Configure(EntityTypeBuilder<RoleMenuItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleMenuItems");
        builder.HasKey(rm => new { rm.RoleId, rm.MenuItemId });

        builder.HasOne(rm => rm.MenuItem)
            .WithMany()
            .HasForeignKey(rm => rm.MenuItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rm => rm.MenuItemId).HasDatabaseName("IX_RoleMenuItems_MenuItemId");
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        // SHA-256 as lowercase hex is always 64 characters.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreatedByIp).HasMaxLength(64);
        builder.Property(t => t.RevokedByIp).HasMaxLength(64);
        builder.Property(t => t.RevokedReason).HasMaxLength(64);
        builder.Property(t => t.UserAgent).HasMaxLength(512);

        builder.Property(t => t.CreatedUtc).HasColumnType("datetime2(3)");
        builder.Property(t => t.ExpiresUtc).HasColumnType("datetime2(3)");
        builder.Property(t => t.AbsoluteExpiryUtc).HasColumnType("datetime2(3)");
        builder.Property(t => t.RevokedUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("IX_RefreshTokens_TokenHash");
        builder.HasIndex(t => new { t.UserId, t.ExpiresUtc }).HasDatabaseName("IX_RefreshTokens_UserId_ExpiresUtc");

        // Reuse detection revokes an entire family at once, so that lookup needs an index too.
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("IX_RefreshTokens_FamilyId");
    }
}
