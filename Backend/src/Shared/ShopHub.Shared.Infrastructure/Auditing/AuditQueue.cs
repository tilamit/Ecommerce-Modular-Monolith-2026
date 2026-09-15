using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ShopHub.Shared.Infrastructure.Auditing;

/// <summary>
/// A pending audit row, in transport form.
/// <para>
/// Deliberately a plain record rather than the Auditing module's entity: the interceptor
/// runs inside <em>every</em> module's DbContext and none of them may reference Auditing's
/// internals (spec §4.1).
/// </para>
/// </summary>
public sealed record PendingAuditEntry
{
    public required DateTime OccurredUtc { get; init; }

    public required string Action { get; init; }

    public required string Module { get; init; }

    public required string EntityName { get; init; }

    public string? EntityId { get; init; }

    public Guid? UserId { get; init; }

    public string? UserName { get; init; }

    public string? UserRoles { get; init; }

    public string? ScreenName { get; init; }

    public string? HttpMethod { get; init; }

    public string? Path { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }

    public string? CorrelationId { get; init; }

    public string? OldValues { get; init; }

    public string? NewValues { get; init; }

    public string? ChangedColumns { get; init; }
}

/// <summary>
/// Hands audit entries to the background writer (spec §6.6).
/// </summary>
public interface IAuditQueue
{
    /// <summary>
    /// Queues an entry without blocking. Returns false if the queue is full.
    /// <para>
    /// Dropping is the correct behaviour under pressure: spec §6.6 requires that auditing
    /// never sit on the request's critical path and that an audit failure never roll back a
    /// business transaction. Blocking a checkout to record that a checkout happened would
    /// invert the priority.
    /// </para>
    /// </summary>
    bool TryEnqueue(PendingAuditEntry entry);

    /// <summary>Drains queued entries for the background writer.</summary>
    IAsyncEnumerable<PendingAuditEntry> ReadAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Bounded in-memory channel between the request thread and the background writer.
/// </summary>
public sealed class AuditQueue : IAuditQueue
{
    /// <summary>
    /// Bounded on purpose. An unbounded channel turns a database outage into an
    /// out-of-memory crash - the process dies holding the very evidence of what it was
    /// doing. A bounded queue sheds load loudly instead.
    /// </summary>
    private const int Capacity = 10_000;

    private readonly Channel<PendingAuditEntry> _channel = Channel.CreateBounded<PendingAuditEntry>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly ILogger<AuditQueue> _logger;

    private long _droppedCount;

    public AuditQueue(ILogger<AuditQueue> logger) => _logger = logger;

    public bool TryEnqueue(PendingAuditEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
        {
            return true;
        }

        var dropped = Interlocked.Increment(ref _droppedCount);

        // Logged at Error: a dropped audit entry is a gap in the trail and silent gaps
        // are worse than no trail at all because they look complete.
        _logger.LogError(
            "Audit queue full; dropped an entry for {Module}.{EntityName}. Total dropped this run: {DroppedCount}.",
            entry.Module,
            entry.EntityName,
            dropped);

        return false;
    }

    public IAsyncEnumerable<PendingAuditEntry> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
