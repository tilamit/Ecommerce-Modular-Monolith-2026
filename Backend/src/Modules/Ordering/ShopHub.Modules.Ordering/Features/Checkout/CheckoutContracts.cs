using ShopHub.Modules.Ordering.Domain;
using ShopHub.Modules.Ordering.Features.Carts;

namespace ShopHub.Modules.Ordering.Features.Checkout;

internal sealed record AddressRequest(
    string FullName,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country,
    string? PhoneNumber);

/// <summary>Registered checkout (spec §9.3). The cart comes from the server, not the request.</summary>
internal sealed record CheckoutRequest(
    AddressRequest ShippingAddress,
    PaymentMethod PaymentMethod,
    string? OfferCode,
    string? Notes);

/// <summary>
/// Guest checkout (spec §8.4 state 4). Carries its own lines, because a guest has no
/// server-side cart.
/// </summary>
internal sealed record GuestCheckoutRequest(
    string Email,
    string FullName,
    string? PhoneNumber,
    AddressRequest ShippingAddress,
    PaymentMethod PaymentMethod,
    string? OfferCode,
    string? Notes,
    IReadOnlyList<CartLineRequest> Items);

/// <summary>
/// The placed order. <c>AccountExistsPrompt</c> is set when a guest checks out with an
/// email that already has an account (spec §8.4) - a soft prompt only, because the order is
/// deliberately <b>not</b> linked: auto-linking on an unverified email is an
/// account-takeover vector.
/// </summary>
internal sealed record CheckoutResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal ShippingTotal,
    decimal GrandTotal,
    string CurrencyCode,
    string PaymentMethod,
    string PaymentStatus,
    DateTime PlacedUtc,
    IReadOnlyList<SkippedLineResponse> Skipped,
    string? AccountExistsPrompt);

/// <summary>
/// A line that could not be reserved (ADR-002). Checkout fails as a whole and returns one
/// of these per failing line, rather than silently dropping items and changing the total.
/// </summary>
internal sealed record ReservationFailureResponse(
    Guid ProductId,
    string ProductName,
    string Reason,
    int Available,
    int Requested);
