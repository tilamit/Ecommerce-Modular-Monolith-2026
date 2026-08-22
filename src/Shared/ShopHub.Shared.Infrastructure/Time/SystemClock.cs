using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Shared.Infrastructure.Time;

/// <summary>
/// Production <see cref="IClock"/>. Registered as a singleton; tests substitute a fake.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
