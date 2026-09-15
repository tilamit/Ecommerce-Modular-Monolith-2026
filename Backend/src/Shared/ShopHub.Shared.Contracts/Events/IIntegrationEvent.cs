namespace ShopHub.Shared.Contracts.Events;

/// <summary>
/// Marker for a cross-module notification (spec §4.3). Integration events are the
/// asynchronous, fire-and-forget half of module communication; the synchronous half is a
/// call through the other module's <c>*.Contracts</c> interface.
/// <para>
/// Events live here - in Shared.Contracts - rather than in the publishing module, so a
/// consumer never has to reference a producer's implementation assembly.
/// </para>
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Unique id for this occurrence. Gives consumers something to deduplicate on.</summary>
    Guid EventId { get; }

    /// <summary>When the event was raised (UTC).</summary>
    DateTime OccurredUtc { get; }
}

/// <summary>
/// Convenience base carrying the two envelope fields, so concrete events stay
/// declarations of their payload only.
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    protected IntegrationEvent(Guid eventId, DateTime occurredUtc)
    {
        EventId = eventId;
        OccurredUtc = occurredUtc;
    }

    public Guid EventId { get; init; }

    public DateTime OccurredUtc { get; init; }
}
