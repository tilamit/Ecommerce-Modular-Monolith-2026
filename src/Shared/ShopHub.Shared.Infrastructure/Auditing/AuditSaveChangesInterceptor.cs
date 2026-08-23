using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Entities;

namespace ShopHub.Shared.Infrastructure.Auditing;

/// <summary>
/// Captures entity changes into the audit trail (spec §6.6). Registered on <b>every</b>
/// module's DbContext.
/// </summary>
/// <remarks>
/// <para>
/// Two-phase by necessity. <c>SavingChanges</c> snapshots the change set while the change
/// tracker still knows what changed; <c>SavedChanges</c> resolves database-generated keys
/// and only then queues the entries - before the save, a new row has no id to record.
/// </para>
/// <para>
/// Nothing here touches the database. Entries go onto a channel drained by a background
/// service, so auditing never sits on the request's critical path and an audit failure can
/// never roll back a business transaction.
/// </para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor(
    IAuditQueue queue,
    IClock clock,
    ICurrentUser currentUser,
    IAuditContext auditContext,
    string moduleName,
    ILogger<AuditSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    private readonly List<PendingChange> _pending = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Snapshot(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Flush();

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        // The save failed, so nothing happened worth auditing. Dropping the snapshot keeps
        // the interceptor usable if the same context is reused.
        _pending.Clear();

        base.SaveChangesFailed(eventData);
    }

    private void Snapshot(DbContext? context)
    {
        _pending.Clear();

        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Opt-in per entity (spec §6.6). Auditing everything is noise, and noise in an
            // audit trail is indistinguishable from hiding.
            if (entry.Entity is not IAuditableEntity)
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => "Insert",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => null,
            };

            if (action is null)
            {
                continue;
            }

            _pending.Add(Capture(entry, action));
        }
    }

    private static PendingChange Capture(EntityEntry entry, string action)
    {
        var oldValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var newValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var changed = new List<string>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;

            // Redaction (spec §6.6): a property marked [NoAudit] - PasswordHash,
            // TokenHash - must never reach Old/New values. Writing a secret into the audit
            // trail copies it into a table with looser access than the one it came from.
            if (IsRedacted(entry, name))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    newValues[name] = property.CurrentValue;
                    break;

                case EntityState.Deleted:
                    oldValues[name] = property.OriginalValue;
                    break;

                case EntityState.Modified when property.IsModified:
                    // Changed properties only, never the whole row.
                    oldValues[name] = property.OriginalValue;
                    newValues[name] = property.CurrentValue;
                    changed.Add(name);
                    break;

                default:
                    break;
            }
        }

        return new PendingChange(entry, action, oldValues, newValues, changed);
    }

    /// <summary>
    /// Queues the snapshotted changes, now that database-generated keys are resolved.
    /// </summary>
    private void Flush()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var occurredUtc = clock.UtcNow;
        var userId = currentUser.Id;
        var userName = currentUser.Name;
        var roles = currentUser.Roles.Count > 0 ? string.Join(",", currentUser.Roles) : null;

        foreach (var change in _pending)
        {
            try
            {
                queue.TryEnqueue(new PendingAuditEntry
                {
                    OccurredUtc = occurredUtc,
                    Action = change.Action,
                    Module = moduleName,
                    EntityName = change.Entry.Entity.GetType().Name,
                    EntityId = ResolveKey(change.Entry),
                    UserId = userId,
                    UserName = userName,
                    UserRoles = roles,
                    ScreenName = auditContext.ScreenName,
                    HttpMethod = auditContext.HttpMethod,
                    Path = auditContext.Path,
                    IpAddress = auditContext.IpAddress,
                    UserAgent = auditContext.UserAgent,
                    CorrelationId = auditContext.CorrelationId,
                    OldValues = Serialize(change.OldValues),
                    NewValues = Serialize(change.NewValues),
                    ChangedColumns = change.Changed.Count > 0 ? JsonSerializer.Serialize(change.Changed) : null,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let an audit problem surface as a failed business operation
                // (spec §6.6: "log it, drop it, alert").
                logger.LogError(ex, "Failed to queue an audit entry for {Module}.", moduleName);
            }
        }

        _pending.Clear();
    }

    private static bool IsRedacted(EntityEntry entry, string propertyName) =>
        entry.Entity.GetType()
            .GetProperty(propertyName)?
            .IsDefined(typeof(NoAuditAttribute), inherit: true) == true;

    private static string? ResolveKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();

        if (key is null)
        {
            return null;
        }

        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => v is not null);

        return string.Join("|", values);
    }

    private static string? Serialize(Dictionary<string, object?> values) =>
        values.Count == 0 ? null : JsonSerializer.Serialize(values);

    /// <summary>A change captured before the save, held until keys are resolved.</summary>
    private sealed record PendingChange(
        EntityEntry Entry,
        string Action,
        Dictionary<string, object?> OldValues,
        Dictionary<string, object?> NewValues,
        List<string> Changed);
}
