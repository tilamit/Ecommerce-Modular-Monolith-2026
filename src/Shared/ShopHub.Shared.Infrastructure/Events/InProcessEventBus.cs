using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopHub.Shared.Contracts.Events;

namespace ShopHub.Shared.Infrastructure.Events;

/// <summary>
/// Delivers integration events by resolving handlers from DI and awaiting them in-process.
/// <para>
/// Each handler runs in its <em>own</em> DI scope. That matters: a handler in the consuming
/// module must not share the publisher's DbContext, or a module would be participating in
/// another module's transaction - exactly the coupling spec §4.3 forbids.
/// </para>
/// <para>
/// A failing handler is logged and swallowed so one bad consumer cannot fail the publisher's
/// request. This is the reliability gap the spec names: if a dropped event ever matters,
/// the fix is an outbox behind <see cref="IEventBus"/>, not a change here.
/// </para>
/// </summary>
internal sealed class InProcessEventBus(IServiceScopeFactory scopeFactory, ILogger<InProcessEventBus> logger)
    : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await using var scope = scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().ToArray();

        if (handlers.Length == 0)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "No handler registered for {EventType} ({EventId}).",
                    typeof(TEvent).Name,
                    integrationEvent.EventId);
            }

            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(integrationEvent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Handler {Handler} failed for {EventType} ({EventId}). The event was dropped.",
                    handler.GetType().Name,
                    typeof(TEvent).Name,
                    integrationEvent.EventId);
            }
        }
    }
}
