namespace ShopHub.Shared.Contracts.Events;

/// <summary>
/// Raised after an order commits (spec §9.3).
/// <para>
/// Lives in Shared.Contracts rather than in Ordering, so a consumer - Auditing today,
/// anything else tomorrow - can handle it without referencing the producer's implementation
/// assembly. That is the property that lets Ordering be extracted later without touching
/// its consumers.
/// </para>
/// <para>
/// Carries a snapshot of the lines, not ids to look up. A handler running after the fact
/// should not have to re-query for facts that were true at publication.
/// </para>
/// </summary>
public sealed record OrderPlacedIntegrationEvent(
    Guid EventId,
    DateTime OccurredUtc,
    Guid OrderId,
    string OrderNumber,
    Guid? UserId,
    Guid? GuestProfileId,
    decimal GrandTotal,
    string CurrencyCode,
    DateTime PlacedUtc,
    IReadOnlyList<OrderPlacedLine> Lines) : IntegrationEvent(EventId, OccurredUtc);

/// <summary>One line of a placed order, as of placement.</summary>
public sealed record OrderPlacedLine(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>
/// Raised when a new account is created (spec §4.3). Ordering consumes it to link any
/// guest-checkout history for that email to the new account (spec §8.4).
/// </summary>
public sealed record UserRegisteredIntegrationEvent(
    Guid EventId,
    DateTime OccurredUtc,
    Guid UserId,
    string Email,
    string FullName) : IntegrationEvent(EventId, OccurredUtc);
