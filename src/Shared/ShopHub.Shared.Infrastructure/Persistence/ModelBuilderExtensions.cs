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
    /// <para>
    /// Spec A5 has the application generate sequential v7 GUIDs. EF's default convention
    /// for a <c>Guid</c> key is <c>ValueGeneratedOnAdd</c>, which means EF infers state from
    /// the key: a non-default value is taken as "this row already exists".
    /// </para>
    /// <para>
    /// That inference is wrong here, and it fails <em>silently and selectively</em>. An
    /// entity added through <c>DbSet.Add</c> is explicitly marked Added and inserts fine.
    /// One reached only through a parent's navigation - a cart item, an order line - is
    /// classified by key value alone, so EF emits an <c>UPDATE ... WHERE Id = @id</c> against
    /// a row that does not exist yet, and the save fails with a
    /// <c>DbUpdateConcurrencyException</c> that has nothing to do with concurrency.
    /// </para>
    /// <para>
    /// <c>ValueGeneratedNever</c> tells EF the truth: the application owns these keys, so
    /// state comes from the change tracker rather than from guessing. Identity columns
    /// (<c>bigint</c> on <c>audit.AuditTrails</c>) are deliberately untouched - those really
    /// are store-generated.
    /// </para>
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
