using ShopHub.Shared.Contracts.Events;

namespace ShopHub.Shared.Infrastructure.Events;

/// <summary>
/// In-process publish side of module-to-module notification (spec §4.3).
/// <para>
/// Publishing is deliberately isolated behind this one interface so the delivery
/// mechanism can be swapped without touching a single call site - to a transactional
/// outbox if reliability gaps appear (spec §4.3, Phase 5), or to a real broker when a
/// module is extracted into its own service (spec §3, the migration test).
/// </para>
/// <para>
/// Publish <em>after</em> the publishing module's transaction commits. An event announcing
/// a change that then rolls back is worse than no event at all.
/// </para>
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;
}

/// <summary>
/// Handles an integration event raised by another module. Implementations live in the
/// <em>consuming</em> module's <c>EventHandlers/</c> folder and are registered by that
/// module's <c>IModule.RegisterModule</c>.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class, IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
