using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using ShopHub.Api.Extensions;
using ShopHub.Modules.Auditing;
using ShopHub.Modules.Catalog;
using ShopHub.Modules.Identity;
using ShopHub.Modules.Ordering;
using ShopHub.Shared.Infrastructure;
using ShopHub.Shared.Infrastructure.Modules;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Exceptions;

// Bootstrap logger first, so a failure in configuration binding below is still logged.
LoggingExtensions.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.ConfigureSerilog();

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
    builder.Services.AddForwardedHeaders(builder.Configuration);

    foreach (var module in modules)
    {
        module.RegisterModule(builder.Services, builder.Configuration);
    }

    var app = builder.Build();

    // --- Pipeline -----------------------------------------------------------
    // Order matters. Exception handling is outermost so it catches everything
    // downstream; correlation sits just inside it so even a 500 is logged under
    // the caller's correlation id.
    app.UseExceptionHandler();

    if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    {
        app.UseForwardedHeaders();
    }

    app.UseCorrelationId();
    app.UseShopHubRequestLogging();
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

        app.MapDiagnosticsEndpoints();
    }

    foreach (var module in modules)
    {
        module.MapEndpoints(app);
    }

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
