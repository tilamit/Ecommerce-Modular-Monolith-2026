namespace ShopHub.Shared.Kernel.Abstractions;

/// <summary>
/// The only source of "now" in the solution. Never call <c>DateTime.UtcNow</c> directly -
/// token expiry, lockout windows and cart expiry all need to be steerable from a test.
/// All values are UTC (spec A6).
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today { get; }
}
