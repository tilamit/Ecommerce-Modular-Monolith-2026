using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Shared.Infrastructure.Caching;
using ShopHub.Shared.Infrastructure.Correlation;
using ShopHub.Shared.Infrastructure.Errors;
using ShopHub.Shared.Infrastructure.Events;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Infrastructure.Time;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Shared.Infrastructure;

/// <summary>
/// Registers every cross-cutting concern from spec §6, once, for the whole host.
/// Modules consume these; modules do not configure them.
/// </summary>
public static class SharedInfrastructureExtensions
{
    public static IServiceCollection AddSharedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuditContext, RequestAuditContext>();
        services.AddSingleton<IEventBus, InProcessEventBus>();

        // Singleton: the queue spans requests, bridging the per-request interceptor to the
        // single background writer that drains it (spec §6.6).
        services.AddSingleton<Auditing.IAuditQueue, Auditing.AuditQueue>();

        services.AddShopHubProblemDetails();
        services.AddShopHubCaching(configuration);
        services.AddShopHubRateLimiting(configuration);

        return services;
    }

    /// <summary>
    /// ProblemDetails plus the handler that maps exceptions onto it (spec §6.1).
    /// </summary>
    private static IServiceCollection AddShopHubProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Applies to framework-generated problems too (404 from routing, 415, and
                // so on), so every error response carries the same traceId contract.
                context.ProblemDetails.Extensions.TryAdd(
                    "traceId",
                    System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    /// <summary>
    /// HybridCache, L1 only (spec §6.4).
    /// <para>
    /// No <c>IDistributedCache</c> is registered, so this runs purely in-process. When
    /// Redis is wanted later, registering it at startup is the entire change - nothing
    /// here and no call site moves.
    /// </para>
    /// </summary>
    private static IServiceCollection AddShopHubCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CachingOptions.SectionName).Get<CachingOptions>() ?? new CachingOptions();

        services.AddHybridCache(cache =>
        {
            cache.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(options.DefaultExpirationMinutes),
                LocalCacheExpiration = TimeSpan.FromMinutes(options.LocalExpirationMinutes),
            };

            cache.MaximumPayloadBytes = options.MaximumPayloadBytes;
        });

        return services;
    }

    /// <summary>
    /// Correlation id in, correlation id out (spec §6.2). Register this early in the
    /// pipeline so everything downstream - including the exception handler - logs under it.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}

/// <summary>
/// Bound from the <c>Caching</c> configuration section (spec §6.4 defaults).
/// </summary>
public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    public int DefaultExpirationMinutes { get; set; } = 5;

    public int LocalExpirationMinutes { get; set; } = 2;

    /// <summary>Caps a single entry at 1 MB, so one oversized payload cannot evict everything else.</summary>
    public long MaximumPayloadBytes { get; set; } = 1024 * 1024;
}
