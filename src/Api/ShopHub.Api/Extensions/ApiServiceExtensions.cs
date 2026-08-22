using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Net.Http.Headers;

namespace ShopHub.Api.Extensions;

/// <summary>
/// Host-level services that are not owned by any module: CORS, OpenAPI, health,
/// compression, output caching, and JSON conventions.
/// </summary>
internal static class ApiServiceExtensions
{
    internal const string SpaCorsPolicy = "shophub-spa";

    internal static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiCors(configuration);
        services.AddApiOpenApi();
        services.AddApiHealthChecks(configuration);
        services.AddApiCompression();
        services.AddOutputCache();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            // Enums cross the wire as names, not ordinals - an ordinal silently changes
            // meaning the day someone inserts a value in the middle of the enum.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        return services;
    }

    /// <summary>
    /// CORS for the SPA (spec §11.5).
    /// <para>
    /// The refresh token travels as a cookie, so the policy needs
    /// <c>AllowCredentials</c> - which makes an explicit origin list mandatory.
    /// <c>AllowAnyOrigin</c> with credentials is rejected by both the framework and the
    /// browser, so there is no shortcut available here even in Development.
    /// </para>
    /// </summary>
    private static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy =>
            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders(
                    Shared.Infrastructure.Correlation.ShopHubHeaders.CorrelationId,
                    HeaderNames.RetryAfter)));

        return services;
    }

    /// <summary>
    /// First-party OpenAPI document generation (spec §2). Swashbuckle's generator is
    /// deliberately absent - only a UI layer is added on top, in Program.cs.
    /// </summary>
    private static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new Microsoft.OpenApi.OpenApiInfo
            {
                Title = "ShopHub API",
                Version = "v1",
                Description = "Modular monolith e-commerce API. Each module owns its own schema and endpoints.",
            };

            return Task.CompletedTask;
        }));

        return services;
    }

    /// <summary>
    /// Liveness and readiness (spec §2). Liveness answers "is the process up"; readiness
    /// answers "can it serve traffic", which is why only readiness probes the database.
    /// </summary>
    private static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddHealthChecks();
        var connectionString = configuration.GetConnectionString("ShopHub");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.AddSqlServer(
                connectionString,
                name: "sqlserver",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "db"]);
        }

        return services;
    }

    private static IServiceCollection AddApiCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        return services;
    }

    /// <summary>
    /// Forwarded headers, off unless explicitly configured. The rate limiter partitions on
    /// the socket address precisely because trusting a spoofable header would let a caller
    /// mint a new partition per request (spec §6.3).
    /// </summary>
    internal static IServiceCollection AddForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
        {
            return services;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }
}
