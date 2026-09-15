using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ShopHub.Shared.Infrastructure.Persistence;

/// <summary>
/// Model conventions every module's DbContext applies, so they cannot drift apart.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Marks <see cref="Guid"/> primary keys as application-generated.
    /// </summary>
    /// <remarks>
    /// Spec A5 has the application generate sequential v7 GUIDs. EF's default convention for
    /// a <c>Guid</c> key is <c>ValueGeneratedOnAdd</c>, which infers entity state from the
    /// key value, so a non-default value reads as "this row already exists". Entities added
    /// through <c>DbSet.Add</c> are fine, but one reached only through a parent navigation
    /// (a cart item, an order line) is classified by key alone: EF emits an <c>UPDATE</c>
    /// against a row that does not exist and the save fails with a
    /// <c>DbUpdateConcurrencyException</c> unrelated to concurrency.
    /// <c>ValueGeneratedNever</c> makes state come from the change tracker. Identity columns
    /// (<c>bigint</c> on <c>audit.AuditTrails</c>) are left alone, being genuinely
    /// store-generated.
    /// </remarks>
    public static ModelBuilder UseApplicationGeneratedGuidKeys(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();

            if (key is null)
            {
                continue;
            }

            foreach (var property in key.Properties.Where(p => p.ClrType == typeof(Guid)))
            {
                property.ValueGenerated = ValueGenerated.Never;
            }
        }

        return modelBuilder;
    }
}
