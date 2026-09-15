using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Modules.Ordering.Domain;
using ShopHub.Modules.Ordering.Persistence;
using ShopHub.Shared.Contracts.Events;
using ShopHub.Shared.Infrastructure.Events;

namespace ShopHub.Modules.Ordering.EventHandlers;

/// <summary>
/// Links prior guest-checkout history to a newly registered account (spec §8.4).
/// <para>
/// The only moment linking is allowed. At checkout the email is unverified, so linking there
/// would be an account-takeover vector (ADR-001); at registration the person has shown
/// control of the address by setting a password on it.
/// </para>
/// <para>
/// It never rewrites <c>Orders.UserId</c>: those orders were placed as guest orders and stay
/// that way. Only <c>GuestCheckoutProfiles.LinkedUserId</c> is set, which is enough for
/// analytics to join the two.
/// </para>
/// </summary>
internal sealed class UserRegisteredHandler(OrderingDbContext db, ILogger<UserRegisteredHandler> logger)
    : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public async Task HandleAsync(UserRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var normalized = GuestCheckoutProfile.Normalize(integrationEvent.Email);

        var profile = await db.GuestCheckoutProfiles
            .AsTracking()
            .FirstOrDefaultAsync(g => g.NormalizedEmail == normalized && g.LinkedUserId == null, cancellationToken);

        if (profile is null)
        {
            return;
        }

        profile.LinkToUser(integrationEvent.UserId);
        await db.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Linked guest checkout profile {ProfileId} to newly registered user {UserId}.",
                profile.Id,
                integrationEvent.UserId);
        }
    }
}
