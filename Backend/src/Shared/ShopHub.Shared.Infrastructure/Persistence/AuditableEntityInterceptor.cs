using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Entities;

namespace ShopHub.Shared.Infrastructure.Persistence;

/// <summary>
/// Fills the provenance columns every table carries (spec §8: <c>CreatedUtc</c>,
/// <c>CreatedBy</c>, <c>ModifiedUtc</c>, <c>ModifiedBy</c>) and turns a delete of an
/// <see cref="ISoftDeletable"/> entity into an update (spec A7).
/// <para>
/// This is distinct from the audit <em>trail</em> interceptor of spec §6.6. This one
/// answers "who touched this row last"; the trail answers "what changed, when and from
/// which screen".
/// </para>
/// </summary>
internal sealed class AuditableEntityInterceptor(IClock clock, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyProvenance(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyProvenance(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyProvenance(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var actor = Actor();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            SoftDeleteIfRequested(entry, now, actor);

            if (entry.Entity is not IAuditableColumns auditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.CreatedUtc = now;
                    auditable.CreatedBy = actor;
                    break;

                case EntityState.Modified:
                    auditable.ModifiedUtc = now;
                    auditable.ModifiedBy = actor;
                    // Never let an update rewrite who created the row.
                    entry.Property(nameof(IAuditableColumns.CreatedUtc)).IsModified = false;
                    entry.Property(nameof(IAuditableColumns.CreatedBy)).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Converts <c>Remove()</c> on a soft-deletable entity into a flag update, so a
    /// caller cannot delete a row by forgetting the convention.
    /// </summary>
    private static void SoftDeleteIfRequested(EntityEntry entry, DateTime now, string actor)
    {
        if (entry is { State: EntityState.Deleted, Entity: ISoftDeletable deletable })
        {
            entry.State = EntityState.Modified;
            deletable.IsDeleted = true;
            deletable.DeletedUtc = now;

            if (entry.Entity is IAuditableColumns auditable)
            {
                auditable.ModifiedUtc = now;
                auditable.ModifiedBy = actor;
            }
        }
    }

    /// <summary>
    /// Records the acting user's id, or a system marker for seeding and background
    /// services where there is no request and therefore no user.
    /// </summary>
    private string Actor() => currentUser.Id?.ToString() ?? "system";
}
