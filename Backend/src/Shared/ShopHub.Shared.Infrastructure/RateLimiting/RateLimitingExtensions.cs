using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ShopHub.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Registers the five named policies from spec §6.3 using the built-in rate limiter -
/// no third-party package.
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddShopHubRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            var options = configuration
                .GetSection(RateLimitingOptions.SectionName)
                .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(RateLimitPolicies.Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(RateLimitPolicies.Auth, ClientIp(context)),
                    _ => FixedWindow(options.Auth)));

            limiter.AddPolicy(RateLimitPolicies.Anonymous, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    PartitionKey(RateLimitPolicies.Anonymous, ClientIp(context)),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.Anonymous.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.Anonymous.WindowSeconds),
                        SegmentsPerWindow = options.Anonymous.SegmentsPerWindow,
                        QueueLimit = options.Anonymous.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }));

            limiter.AddPolicy(RateLimitPolicies.Authenticated, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    PartitionKey(RateLimitPolicies.Authenticated, Subject(context)),
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.Authenticated.TokenLimit,
                        TokensPerPeriod = options.Authenticated.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(options.Authenticated.ReplenishmentPeriodSeconds),
                        AutoReplenishment = true,
                        QueueLimit = options.Authenticated.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }));

            limiter.AddPolicy(RateLimitPolicies.Write, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(RateLimitPolicies.Write, Subject(context)),
                    _ => FixedWindow(options.Write)));

            // Checkout partitions on IP *and* cart, so one shared NAT address cannot
            // rate-limit an unrelated shopper behind it.
            limiter.AddPolicy(RateLimitPolicies.Checkout, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(RateLimitPolicies.Checkout, $"{ClientIp(context)}|{CartId(context)}"),
                    _ => FixedWindow(options.Checkout)));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                // Only some limiters expose a retry hint; emit the header when one is available.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsync(
                    """
                    {"type":"https://httpstatuses.io/429","title":"Too many requests.","status":429,"code":"rate_limited","detail":"You have made too many requests. Slow down and try again shortly."}
                    """,
                    cancellationToken);
            };
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions FixedWindow(FixedWindowPolicyOptions options) => new()
    {
        PermitLimit = options.PermitLimit,
        Window = TimeSpan.FromSeconds(options.WindowSeconds),
        QueueLimit = options.QueueLimit,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    };

    private static string PartitionKey(string policy, string discriminator) => $"{policy}:{discriminator}";

    /// <summary>
    /// Spec §6.3: trust proxy headers only when ForwardedHeaders is configured, otherwise
    /// partition on the socket address. Reading X-Forwarded-For directly here would let any
    /// caller mint a fresh partition per request and defeat the limiter entirely.
    /// </summary>
    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string Subject(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub")
        ?? ClientIp(context);

    private static string CartId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Cart-Id", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : "no-cart";
}
