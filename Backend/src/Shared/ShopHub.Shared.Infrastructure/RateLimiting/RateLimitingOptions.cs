namespace ShopHub.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Names of the rate-limit policies from spec §6.3. Endpoint groups attach one with
/// <c>.RequireRateLimiting(RateLimitPolicies.Auth)</c> - string literals at call sites
/// are how a policy silently stops applying after a rename.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Login, register, refresh, forgot-password. Partitioned by client IP.</summary>
    public const string Auth = "auth";

    /// <summary>Public storefront browsing and search. Partitioned by client IP.</summary>
    public const string Anonymous = "anonymous";

    /// <summary>All authenticated endpoints. Partitioned by the 'sub' claim.</summary>
    public const string Authenticated = "authenticated";

    /// <summary>Admin writes (POST/PUT/DELETE). Partitioned by the 'sub' claim.</summary>
    public const string Write = "write";

    /// <summary>Checkout submission. Partitioned by IP plus cart id.</summary>
    public const string Checkout = "checkout";
}

/// <summary>
/// Bound from the <c>RateLimiting</c> configuration section, so limits are tuned without a
/// rebuild (spec §6.3: "make them config-driven"). Defaults match the spec's table.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowPolicyOptions Auth { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public SlidingWindowPolicyOptions Anonymous { get; set; } = new()
    {
        PermitLimit = 100,
        WindowSeconds = 60,
        SegmentsPerWindow = 4,
    };

    public TokenBucketPolicyOptions Authenticated { get; set; } = new()
    {
        TokenLimit = 200,
        TokensPerPeriod = 50,
        ReplenishmentPeriodSeconds = 10,
    };

    public FixedWindowPolicyOptions Write { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    public FixedWindowPolicyOptions Checkout { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
}

public sealed class FixedWindowPolicyOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }

    /// <summary>Queued requests are off by default: queueing a rate-limited request just moves the latency.</summary>
    public int QueueLimit { get; set; }
}

public sealed class SlidingWindowPolicyOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }

    public int SegmentsPerWindow { get; set; }

    public int QueueLimit { get; set; }
}

public sealed class TokenBucketPolicyOptions
{
    public int TokenLimit { get; set; }

    public int TokensPerPeriod { get; set; }

    public int ReplenishmentPeriodSeconds { get; set; }

    public int QueueLimit { get; set; }
}
