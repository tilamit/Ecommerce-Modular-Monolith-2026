using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using ShopHub.Api.Dashboards;
using ShopHub.Api.Extensions;
using ShopHub.Modules.Auditing;
using ShopHub.Modules.Catalog;
using ShopHub.Modules.Identity;
using ShopHub.Modules.Ordering;
using ShopHub.Shared.Infrastructure;
using ShopHub.Shared.Infrastructure.Errors;
using ShopHub.Shared.Infrastructure.Modules;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Exceptions;

// Bootstrap logger first, so a failure in configuration binding below is still logged.
LoggingExtensions.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.ConfigureSerilog();

    // Kestrel writes the Server header below the middleware pipeline, so removing it in
    // middleware does not work - it has to be switched off here (spec §14 Phase 11).
    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    // Modules are discovered from an explicit list, not by assembly scanning
    // (spec §5.2): explicit is debuggable and keeps startup fast. Adding module #5
    // is one line here and nothing else - see docs/MODULE_TEMPLATE.md.
    IModule[] modules =
    [
        new IdentityModule(),
        new CatalogModule(),
        new OrderingModule(),
        new AuditingModule(),
    ];

    builder.Services.AddSharedInfrastructure(builder.Configuration);
    builder.Services.AddShopHubAuthentication(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);
    builder.Services.AddScoped<DashboardService>();
    builder.Services.AddForwardedHeaders(builder.Configuration);
    builder.Services.AddShopHubHsts();

    foreach (var module in modules)
    {
        module.RegisterModule(builder.Services, builder.Configuration);
    }

    var app = builder.Build();

    // --- Pipeline -----------------------------------------------------------
    // Order matters. Exception handling is outermost so it catches everything
    // downstream; correlation sits just inside it so even a 500 is logged under
    // the caller's correlation id.
    // Registered a second time further down, inside the request logging; this one is the
    // backstop for the middlewares above it, so a failure in correlation or in the request
    // logging itself still produces a ProblemDetails rather than a bare Kestrel 500.
    app.UseExceptionHandler();

    // Early, so a response short-circuited by the rate limiter or an auth challenge still
    // carries the security headers (spec §14 Phase 11).
    app.UseSecurityHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    {
        app.UseForwardedHeaders();
    }

    app.UseCorrelationId();
    app.UseShopHubRequestLogging();

    // Registered inside the request logging so the summary line reports the status the
    // client received. An exception travelling out through Serilog's request-logging
    // middleware has no status yet, so it is recorded as 500 at Error with a stack trace,
    // even though the handler above then answers 404 or 400. Every expected 4xx logged two
    // lines that disagreed, which is what spec §6.1 forbids. Resolving the exception here
    // means the status is already set when the logging middleware regains control and the
    // one stack trace a genuine 500 warrants is left to GlobalExceptionHandler.
    app.UseExceptionHandler();

    // Inside that in turn, so a client hang-up is resolved in first-party code and recorded
    // as the 499 it is - rather than escaping as an unhandled exception, which is both a
    // 500-level log line and a debugger stop on an ordinary page load.
    app.UseClientDisconnectHandling();

    app.UseResponseCompression();
    app.UseCors(ApiServiceExtensions.SpaCorsPolicy);
    app.UseRateLimiter();

    // Authentication before authorization, both after CORS so a preflight is not challenged.
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseOutputCache();

    app.MapHealthEndpoints();

    if (app.Environment.IsDevelopment())
    {
        // Document generation is first-party; Scalar supplies only the UI (spec §2).
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("ShopHub API")
            .WithOpenApiRoutePattern("/openapi/{documentName}.json"));

        // The API reference is the one HTML page this host serves and the API's own
        // `default-src 'none'; sandbox` policy renders it blank - it loads two same-origin
        // scripts, an inline module script and styles the bundle injects at runtime.
        // Setting the policy here rather than skipping it keeps a page served from the API
        // origin under a policy of some kind; SecurityHeadersMiddleware leaves an existing
        // header alone, so this one wins. Development only - MapScalarApiReference is too.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/scalar"))
            {
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                    "style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; " +
                    "font-src 'self' data: https:; connect-src 'self'; frame-ancestors 'none'";
            }

            await next();
        });

        app.MapDiagnosticsEndpoints();
    }

    foreach (var module in modules)
    {
        module.MapEndpoints(app);
    }

    // Cross-module read composition (spec §10). Lives in the host because it spans
    // modules - and can only reach them through their Contracts.
    app.MapDashboardEndpoints();

    // Applies migrations and seeds, Development only. Runs before the first request so a
    // broken migration surfaces at startup rather than as a 500 on someone's first call.
    await app.InitializeModulesAsync();

    Log.Information(
        "ShopHub API starting in {Environment} with modules: {Modules}",
        app.Environment.EnvironmentName,
        string.Join(", ", modules.Select(m => m.Name)));

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "ShopHub API terminated unexpectedly during startup.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Named so <c>WebApplicationFactory&lt;Program&gt;</c> can find the entry point from the
/// integration test project (spec §12).
/// </summary>
public partial class Program;
