using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Modules.Auditing.Domain;

namespace ShopHub.Modules.Auditing.Persistence;

/// <summary>The Auditing module's data (spec §6.6). Append-only.</summary>
internal sealed class AuditingDbContext(DbContextOptions<AuditingDbContext> options) : DbContext(options)
{
    internal const string Schema = "audit";

    internal const string MigrationsHistoryTable = "__EFMigrationsHistory_audit";

    internal DbSet<AuditTrail> AuditTrails => Set<AuditTrail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditingDbContext).Assembly);

        // Note: UseApplicationGeneratedGuidKeys is deliberately NOT applied here. This
        // module's only key is a bigint identity (spec A5), which genuinely is
        // store-generated.
        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class AuditTrailConfiguration : IEntityTypeConfiguration<AuditTrail>
{
    public void Configure(EntityTypeBuilder<AuditTrail> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AuditTrails");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.OccurredUtc).HasColumnType("datetime2(3)");
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Module).HasMaxLength(32).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(128);
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.UserRoles).HasMaxLength(256);
        builder.Property(a => a.ScreenName).HasMaxLength(256);
        builder.Property(a => a.HttpMethod).HasMaxLength(16);
        builder.Property(a => a.Path).HasMaxLength(512);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);
        builder.Property(a => a.CorrelationId).HasMaxLength(128);

        builder.Property(a => a.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(a => a.NewValues).HasColumnType("nvarchar(max)");
        builder.Property(a => a.ChangedColumns).HasColumnType("nvarchar(max)");

        // The four indexes spec §6.6 requires, each shaped to a query the UI actually runs.
        builder.HasIndex(a => a.OccurredUtc)
            .IsDescending(true)
            .HasDatabaseName("IX_AuditTrails_OccurredUtc");

        builder.HasIndex(a => new { a.UserId, a.OccurredUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AuditTrails_UserId_OccurredUtc");

        builder.HasIndex(a => new { a.EntityName, a.EntityId })
            .HasDatabaseName("IX_AuditTrails_EntityName_EntityId");

        builder.HasIndex(a => new { a.Action, a.OccurredUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AuditTrails_Action_OccurredUtc");
    }
}
