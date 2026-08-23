using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Modules.Catalog.Contracts;
using ShopHub.Modules.Identity.Contracts;
using ShopHub.Modules.Ordering.Domain;
using ShopHub.Modules.Ordering.Features.Carts;
using ShopHub.Modules.Ordering.Infrastructure;
using ShopHub.Modules.Ordering.Persistence;
using ShopHub.Shared.Contracts.Events;
using ShopHub.Shared.Infrastructure.Events;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Ordering.Features.Checkout;

/// <summary>
/// Thrown when stock cannot be reserved. Carries a reason per failing line so the endpoint
/// can return the 409 shape ADR-002 specifies, rather than a bare message.
/// </summary>
internal sealed class StockReservationException(IReadOnlyList<ReservationFailureResponse> failures)
    : ShopHubException("insufficient_stock", "One or more items are no longer available in the quantity requested.")
{
    public IReadOnlyList<ReservationFailureResponse> Failures { get; } = failures;
}

/// <summary>
/// Registered and guest checkout (spec §8.4 states 2 and 4, §9.3).
/// </summary>
internal sealed class CheckoutService(
    OrderingDbContext db,
    ICartPricer pricer,
    CartService carts,
    ICatalogModuleApi catalog,
    IIdentityModuleApi identity,
    IOrderNumberGenerator orderNumbers,
    IPaymentProcessor payments,
    IEventBus eventBus,
    IClock clock,
    ILogger<CheckoutService> logger)
{
    /// <summary>Retries on an order-number collision, which the unique index turns into an exception.</summary>
    private const int OrderNumberAttempts = 3;

    internal async Task<CheckoutResponse> CheckoutAsync(
        Guid userId,
        CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var cart = await carts.FindActiveCartAsync(userId, cancellationToken)
            ?? throw new DomainRuleException("cart_empty", "Your cart is empty.");

        if (cart.Items.Count == 0)
        {
            throw new DomainRuleException("cart_empty", "Your cart is empty.");
        }

        var lines = cart.Items.Select(i => new CartLineRequest(i.ProductId, i.Quantity)).ToArray();
        var priced = await pricer.PriceAsync(lines, PricingMode.Checkout, cancellationToken);

        EnsurePurchasable(priced);

        var order = await PlaceAsync(
            priced,
            request.ShippingAddress.ToDomain(),
            request.PaymentMethod,
            request.OfferCode,
            request.Notes,
            userId: userId,
            guestProfileId: null,
            actingUserId: userId,
            cancellationToken);

        // The cart is consumed by the order, so it must not be reused.
        cart.MarkConverted(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        await PublishOrderPlacedAsync(order, cancellationToken);

        return ToResponse(order, priced.Skipped, accountExistsPrompt: null);
    }

    /// <summary>
    /// Guest checkout (spec §8.4 state 4). Upserts a <see cref="GuestCheckoutProfile"/>
    /// keyed on email, then places the order against it.
    /// </summary>
    internal async Task<CheckoutResponse> GuestCheckoutAsync(
        GuestCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            throw new DomainRuleException("cart_empty", "Your cart is empty.");
        }

        var priced = await pricer.PriceAsync(request.Items, PricingMode.Checkout, cancellationToken);
        EnsurePurchasable(priced);

        var address = request.ShippingAddress.ToDomain();
        var profile = await UpsertGuestProfileAsync(request, address, cancellationToken);

        var order = await PlaceAsync(
            priced,
            address,
            request.PaymentMethod,
            request.OfferCode,
            request.Notes,
            userId: null,
            guestProfileId: profile.Id,
            actingUserId: null,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await PublishOrderPlacedAsync(order, cancellationToken);

        // Spec §8.4: if the guest email matches an existing account, do NOT auto-link.
        // Return a soft prompt instead - auto-linking on an unverified email is an
        // account-takeover vector (ADR-001).
        var existingUserId = await identity.FindUserIdByEmailAsync(request.Email, cancellationToken);

        var prompt = existingUserId is null
            ? null
            : "An account already exists for this email. Log in to have this order in your history.";

        return ToResponse(order, priced.Skipped, prompt);
    }

    /// <summary>
    /// Shared placement path: reserve stock, build the order, record its first status.
    /// <para>
    /// Stock is reserved through Catalog's Contracts as an explicit call, not a shared
    /// transaction (spec §8.4). A real system would use a saga or an outbox here; this is
    /// the deliberate simplification, and this method is the named seam where that goes.
    /// </para>
    /// </summary>
    private async Task<Order> PlaceAsync(
        PricedCart priced,
        ShippingAddress address,
        PaymentMethod paymentMethod,
        string? offerCode,
        string? notes,
        Guid? userId,
        Guid? guestProfileId,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        await ReserveStockAsync(priced, cancellationToken);

        var order = await CreateOrderWithUniqueNumberAsync(
            priced,
            address,
            paymentMethod,
            offerCode,
            notes,
            userId,
            guestProfileId,
            actingUserId,
            now,
            cancellationToken);

        var payment = await payments.ProcessAsync(
            new PaymentRequest(order.OrderNumber, order.GrandTotal, order.CurrencyCode, paymentMethod),
            cancellationToken);

        if (!payment.Succeeded)
        {
            // Nothing is charged today (spec A8), but the branch exists so adding a real
            // gateway does not require re-deriving what to do on a decline.
            await ReleaseStockAsync(priced, cancellationToken);

            throw new DomainRuleException("payment_declined", payment.DeclineReason ?? "Payment was declined.");
        }

        return order;
    }

    private async Task ReserveStockAsync(PricedCart priced, CancellationToken cancellationToken)
    {
        var reservation = await catalog.ReserveStockAsync(
            [.. priced.Lines.Select(l => new StockReservationLine(l.ProductId, l.Quantity))],
            cancellationToken);

        if (reservation.Succeeded)
        {
            return;
        }

        // ADR-002: block the whole checkout with a reason per line. Silently removing a
        // line would change the total after the customer reviewed it.
        throw new StockReservationException(
            [.. reservation.Failures.Select(f => new ReservationFailureResponse(
                f.ProductId,
                f.ProductName,
                f.Reason,
                f.Available,
                f.Requested))]);
    }

    private async Task ReleaseStockAsync(PricedCart priced, CancellationToken cancellationToken) =>
        await catalog.ReleaseStockAsync(
            [.. priced.Lines.Select(l => new StockReservationLine(l.ProductId, l.Quantity))],
            cancellationToken);

    /// <summary>
    /// Builds and saves the order, retrying if the generated order number collides.
    /// The unique index is the arbiter, so two concurrent checkouts cannot share a number.
    /// </summary>
    private async Task<Order> CreateOrderWithUniqueNumberAsync(
        PricedCart priced,
        ShippingAddress address,
        PaymentMethod paymentMethod,
        string? offerCode,
        string? notes,
        Guid? userId,
        Guid? guestProfileId,
        Guid? actingUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var orderNumber = await orderNumbers.NextAsync(now, cancellationToken);

            var order = userId is { } customerId
                ? Order.PlaceForCustomer(orderNumber, customerId, priced.CurrencyCode, paymentMethod, address, notes, now)
                : Order.PlaceForGuest(orderNumber, guestProfileId!.Value, priced.CurrencyCode, paymentMethod, address, notes, now);

            foreach (var line in priced.Lines)
            {
                // Snapshot name, SKU and price as of now (spec §4.2): the order must not
                // change when a product is later renamed or repriced.
                order.AddItem(line.ProductId, line.Name, line.Sku, line.UnitPrice, line.Quantity);
            }

            // Discounts, tax and shipping are all zero today - no offer engine at checkout,
            // no tax jurisdiction model, free shipping. ApplyTotals is still the one place
            // totals are computed, so adding any of them is a change here and nowhere else.
            order.ApplyTotals(discountTotal: 0m, taxTotal: 0m, shippingTotal: 0m, appliedOfferCode: offerCode);
            order.RecordPlacement(now, actingUserId);

            db.Orders.Add(order);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return order;
            }
            catch (DbUpdateException) when (attempt < OrderNumberAttempts)
            {
                logger.LogWarning(
                    "Order number {OrderNumber} collided on attempt {Attempt}; retrying.",
                    orderNumber,
                    attempt);

                db.Entry(order).State = EntityState.Detached;
            }
        }
    }

    private async Task<GuestCheckoutProfile> UpsertGuestProfileAsync(
        GuestCheckoutRequest request,
        ShippingAddress address,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var normalized = GuestCheckoutProfile.Normalize(request.Email);

        var profile = await db.GuestCheckoutProfiles
            .AsTracking()
            .FirstOrDefaultAsync(g => g.NormalizedEmail == normalized, cancellationToken);

        if (profile is null)
        {
            profile = GuestCheckoutProfile.Create(
                request.Email,
                request.FullName,
                request.PhoneNumber,
                address,
                request.PaymentMethod,
                now);

            db.GuestCheckoutProfiles.Add(profile);
        }

        // Refresh from the latest form and count the order (spec §8.4).
        profile.RecordCheckout(request.FullName, request.PhoneNumber, address, request.PaymentMethod, now);

        return profile;
    }

    /// <summary>
    /// Publishes after the transaction commits (spec §4.3). An event announcing an order
    /// that then rolls back is worse than no event at all.
    /// </summary>
    private async Task PublishOrderPlacedAsync(Order order, CancellationToken cancellationToken) =>
        await eventBus.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.CreateVersion7(),
                clock.UtcNow,
                order.Id,
                order.OrderNumber,
                order.UserId,
                order.GuestCheckoutProfileId,
                order.GrandTotal,
                order.CurrencyCode,
                order.PlacedUtc,
                [.. order.Items.Select(i => new OrderPlacedLine(i.ProductId, i.ProductNameSnapshot, i.Quantity, i.UnitPrice))]),
            cancellationToken);

    /// <summary>
    /// Refuses a checkout that cannot be honoured exactly as asked (ADR-002).
    /// <para>
    /// A line dropped during re-pricing - deleted, deactivated, or out of stock - is a
    /// <em>failure</em> at checkout, not a silent omission. Proceeding without it would
    /// charge the customer a total they never reviewed for an order they did not place.
    /// </para>
    /// </summary>
    private static void EnsurePurchasable(PricedCart priced)
    {
        if (priced.Skipped.Count > 0)
        {
            throw new StockReservationException(
                [.. priced.Skipped.Select(s => new ReservationFailureResponse(
                    s.ProductId,
                    s.Name ?? "(unknown product)",
                    s.Reason,
                    Available: 0,
                    Requested: 0))]);
        }

        if (priced.Lines.Count == 0)
        {
            throw new DomainRuleException(
                "no_purchasable_items",
                "None of the items in your cart are available any more.");
        }
    }

    private static CheckoutResponse ToResponse(
        Order order,
        IReadOnlyList<SkippedLineResponse> skipped,
        string? accountExistsPrompt) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.SubTotal,
            order.DiscountTotal,
            order.TaxTotal,
            order.ShippingTotal,
            order.GrandTotal,
            order.CurrencyCode,
            order.PaymentMethod.ToString(),
            order.PaymentStatus.ToString(),
            order.PlacedUtc,
            skipped,
            accountExistsPrompt);
}

internal static class AddressRequestExtensions
{
    internal static ShippingAddress ToDomain(this AddressRequest request) =>
        new(
            request.FullName.Trim(),
            request.Line1.Trim(),
            request.Line2?.Trim(),
            request.City.Trim(),
            request.State?.Trim(),
            request.PostalCode.Trim(),
            request.Country.Trim(),
            request.PhoneNumber?.Trim());
}
