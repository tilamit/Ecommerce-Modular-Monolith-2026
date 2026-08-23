using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Auditing.Domain;
using ShopHub.Modules.Auditing.Persistence;
using ShopHub.Shared.Infrastructure.Auditing;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Auditing.Infrastructure;

/// <summary>
/// Drains the audit queue and bulk-inserts into <c>audit.AuditTrails</c> (spec §6.6).
/// </summary>
/// <remarks>
/// <para>
/// Batched rather than row-at-a-time: the trail is the highest-volume table in the system,
/// and one round trip per entry would make auditing the dominant database cost.
/// </para>
/// <para>
/// A write failure is logged and the batch is dropped. That is the spec's instruction -
/// "log it, drop it, alert" - and it is the right trade: the alternative is retrying
/// forever behind a queue that fills, which converts a logging problem into an outage.
/// </para>
/// </remarks>
internal sealed class AuditTrailWriter(
    IAuditQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditTrailWriter> logger) : BackgroundService
{
    /// <summary>Rows per insert batch.</summary>
    private const int BatchSize = 100;

    /// <summary>How long to wait for a batch to fill before writing what is there.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<PendingAuditEntry>(BatchSize);
        using var timer = new PeriodicTimer(FlushInterval);

        var drain = DrainAsync(batch, stoppingToken);
        var flush = FlushOnIntervalAsync(timer, batch, stoppingToken);

        await Task.WhenAll(drain, flush);
    }

    private async Task DrainAsync(List<PendingAuditEntry> batch, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var entry in queue.ReadAllAsync(stoppingToken))
            {
                lock (batch)
                {
                    batch.Add(entry);
                }

                if (batch.Count >= BatchSize)
                {
                    await WriteBatchAsync(batch, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Write whatever is still buffered rather than losing it.
            await WriteBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task FlushOnIntervalAsync(
        PeriodicTimer timer,
        List<PendingAuditEntry> batch,
        CancellationToken stoppingToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await WriteBatchAsync(batch, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task WriteBatchAsync(List<PendingAuditEntry> batch, CancellationToken cancellationToken)
    {
        PendingAuditEntry[] toWrite;

        lock (batch)
        {
            if (batch.Count == 0)
            {
                return;
            }

            toWrite = [.. batch];
            batch.Clear();
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditingDbContext>();

            db.AuditTrails.AddRange(toWrite.Select(ToEntity));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to write {Count} audit entries. The batch was dropped.",
                toWrite.Length);
        }
    }

    private static AuditTrail ToEntity(PendingAuditEntry entry) =>
        AuditTrail.Create(
            entry.OccurredUtc,
            Enum.TryParse<AuditAction>(entry.Action, out var action) ? action : AuditAction.Update,
            entry.Module,
            entry.EntityName,
            entry.EntityId,
            entry.UserId,
            entry.UserName,
            entry.UserRoles,
            entry.ScreenName,
            entry.HttpMethod,
            entry.Path,
            entry.IpAddress,
            entry.UserAgent,
            entry.CorrelationId,
            entry.OldValues,
            entry.NewValues,
            entry.ChangedColumns);
}

/// <summary>
/// The Contracts implementation for non-EF events (spec §9.4). Enriches the caller's entry
/// from the current request, then hands it to the same queue the interceptor uses.
/// </summary>
internal sealed class AuditWriter(
    IAuditQueue queue,
    IClock clock,
    ICurrentUser currentUser,
    IAuditContext auditContext) : IAuditWriter
{
    public void Write(AuditEntryDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        queue.TryEnqueue(new PendingAuditEntry
        {
            OccurredUtc = clock.UtcNow,
            Action = entry.Action.ToString(),
            Module = entry.Module,
            EntityName = entry.EntityName,
            EntityId = entry.EntityId,

            // The caller may override the actor: a failed login has no authenticated user,
            // but the attempted identity is exactly what makes the entry worth having.
            UserId = entry.UserId ?? currentUser.Id,
            UserName = entry.UserName ?? currentUser.Name,
            UserRoles = currentUser.Roles.Count > 0 ? string.Join(",", currentUser.Roles) : null,

            ScreenName = auditContext.ScreenName,
            HttpMethod = auditContext.HttpMethod,
            Path = auditContext.Path,
            IpAddress = auditContext.IpAddress,
            UserAgent = auditContext.UserAgent,
            CorrelationId = auditContext.CorrelationId,
            OldValues = entry.OldValues,
            NewValues = entry.NewValues,
            ChangedColumns = entry.ChangedColumns,
        });
    }
}

/// <summary>
/// Auditing's read-side Contracts implementation, for the admin dashboard tile (spec §10.1).
/// </summary>
internal sealed class AuditModuleApi(AuditingDbContext db) : IAuditModuleApi
{
    public async Task<IReadOnlyList<RecentAuditDto>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        await db.AuditTrails
            .OrderByDescending(a => a.Id)
            .Take(Math.Clamp(take, 1, 50))
            .Select(a => new RecentAuditDto(
                a.Id,
                a.OccurredUtc,
                a.Action.ToString(),
                a.Module,
                a.EntityName,
                a.UserName,
                a.ScreenName))
            .ToListAsync(cancellationToken);
}
