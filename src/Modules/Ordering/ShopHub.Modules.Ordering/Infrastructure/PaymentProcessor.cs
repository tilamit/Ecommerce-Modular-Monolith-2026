using ShopHub.Modules.Ordering.Domain;

namespace ShopHub.Modules.Ordering.Infrastructure;

/// <summary>
/// The payment seam (spec A8).
/// <para>
/// No gateway is integrated. This interface exists so that adding one later is a
/// registration change rather than surgery on the checkout flow - the ordering code already
/// calls it and already handles a declined result.
/// </para>
/// </summary>
internal interface IPaymentProcessor
{
    Task<PaymentResult> ProcessAsync(PaymentRequest request, CancellationToken cancellationToken);
}

internal sealed record PaymentRequest(
    string OrderNumber,
    decimal Amount,
    string CurrencyCode,
    PaymentMethod Method);

internal sealed record PaymentResult(PaymentStatus Status, string? Reference, string? DeclineReason)
{
    public bool Succeeded => Status is PaymentStatus.NotApplicable or PaymentStatus.Paid;
}

/// <summary>
/// Records the chosen method and does nothing else (spec A8: <c>PaymentStatus</c> is always
/// <c>NotApplicable</c> for now).
/// </summary>
internal sealed class NoOpPaymentProcessor : IPaymentProcessor
{
    public Task<PaymentResult> ProcessAsync(PaymentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentResult(PaymentStatus.NotApplicable, Reference: null, DeclineReason: null));
}

/// <summary>
/// Generates human-readable order numbers, e.g. <c>ORD-20260823-000147</c> (spec §8.3).
/// </summary>
internal interface IOrderNumberGenerator
{
    Task<string> NextAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
