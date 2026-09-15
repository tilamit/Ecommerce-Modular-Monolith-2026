using System.Globalization;
using System.Security.Claims;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using ShopHub.Shared.Infrastructure.Correlation;
using ShopHub.Shared.Infrastructure.Errors;

namespace ShopHub.Api.Extensions;

/// <summary>
/// Serilog composition (spec §6.2).
/// </summary>
internal static class LoggingExtensions
{
    private const string LogFilePath = "logs/shophub-.log";
    private const int RetainedFileCountLimit = 7;

    /// <summary>
    /// Bootstrap logger, created <em>before</em> the host builder so a failure during
    /// startup configuration is still written somewhere. Replaced by the full
    /// configuration-driven logger once the host exists.
    /// </summary>
    internal static void CreateBootstrapLogger() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .CreateBootstrapLogger();

    internal static void ConfigureSerilog(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "ShopHub.Api")
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

            if (builder.Environment.IsDevelopment())
            {
                configuration.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
                configuration.WriteTo.File(
                    LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCountLimit,
                    formatProvider: CultureInfo.InvariantCulture);
            }
            else
            {
                // Compact JSON outside Development, so a log shipper can parse it
                // without a grok pattern per message template.
                configuration.WriteTo.Console(new CompactJsonFormatter());
                configuration.WriteTo.File(
                    new CompactJsonFormatter(),
                    LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCountLimit);
            }
        });
    }

    /// <summary>
    /// One summary line per request, enriched with the fields spec §6.2 asks for.
    /// <para>
    /// Level follows status - 5xx Error, 4xx Warning - so expected validation failures do
    /// not drown the log. Health probes drop to Verbose because they run constantly and
    /// would otherwise be the majority of every log file.
    /// </para>
    /// </summary>
    internal static IApplicationBuilder UseShopHubRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.GetLevel = static (httpContext, elapsed, exception) =>
            {
                if (exception is not null || httpContext.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                // 499 is a client hang-up, not a failure: the shopper navigated away or
                // typed another character. Warning-level would make normal browsing look
                // like an incident.
                if (httpContext.Response.StatusCode == ShopHubStatusCodes.ClientClosedRequest)
                {
                    return LogEventLevel.Debug;
                }

                if (httpContext.Response.StatusCode >= 400)
                {
                    return LogEventLevel.Warning;
                }

                // Health probes run constantly; logging each one at Information buries everything else.
                if (httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                {
                    return LogEventLevel.Verbose;
                }

                return LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = static (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("UserId", httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
                diagnosticContext.Set("ClientPage", Header(httpContext, ShopHubHeaders.ClientPage));
                diagnosticContext.Set("CorrelationId", Header(httpContext, ShopHubHeaders.CorrelationId));
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
            };
        });

    private static string? Header(HttpContext context, string name) =>
        context.Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;
}
