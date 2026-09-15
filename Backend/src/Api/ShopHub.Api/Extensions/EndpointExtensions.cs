using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Api.Extensions;

/// <summary>
/// Host-level endpoints: the health probes and the Development-only diagnostics used to
/// demonstrate the Phase 1 acceptance criteria.
/// </summary>
internal static class EndpointExtensions
{
    /// <summary>
    /// Liveness and readiness (spec §2).
    /// <para>
    /// <c>/health/live</c> deliberately runs <em>no</em> checks: it answers "is this
    /// process alive" and a liveness probe that fails on a database blip gets the
    /// container restarted for a problem restarting cannot fix.
    /// </para>
    /// </summary>
    internal static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponse,
        });

        return endpoints;
    }

    /// <summary>
    /// Development-only probes for the two Phase 1 acceptance criteria that cannot be
    /// asserted from code alone: that an unhandled exception produces a clean
    /// ProblemDetails with a traceId and that hammering an endpoint returns 429.
    /// <para>
    /// Mapped inside <c>if (app.Environment.IsDevelopment())</c>, so these do not exist in
    /// any other environment.
    /// </para>
    /// </summary>
    internal static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/_diagnostics")
            .WithTags("Diagnostics")
            .ExcludeFromDescription();

        group.MapGet("/throw/{kind}", (string kind) =>
        {
            throw kind.ToLowerInvariant() switch
            {
                "notfound" => new NotFoundException("Deliberate NotFoundException from the diagnostics endpoint."),
                "conflict" => new ConflictException("Deliberate ConflictException from the diagnostics endpoint."),
                "forbidden" => new ForbiddenException("Deliberate ForbiddenException from the diagnostics endpoint."),
                "domainrule" => new DomainRuleException("Deliberate DomainRuleException from the diagnostics endpoint."),
                _ => (Exception)new InvalidOperationException(
                    "Deliberate unhandled exception from the diagnostics endpoint."),
            };
        });

        // Attached to the strictest policy (5 requests / minute) so the 429 path is
        // reachable by hand without hammering a real endpoint.
        group.MapGet("/rate-limited", () => Results.Ok(new { ok = true }))
            .RequireRateLimiting(RateLimitPolicies.Auth);

        return group;
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,
                // Never surface entry.Value.Exception - a failed SQL probe's message
                // carries the server name and, depending on the driver, the credentials.
                description = entry.Value.Status == HealthStatus.Healthy ? null : "unhealthy",
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
