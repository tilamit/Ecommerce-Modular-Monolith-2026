using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Hosts the real API in-process for integration tests (spec §12).
/// <para>
/// Runs as Development so the OpenAPI document, the Scalar UI, and the diagnostics
/// endpoints used by the Phase 1 acceptance tests are all mapped - the same surface a
/// developer sees locally.
/// </para>
/// </summary>
public sealed class ShopHubApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // An explicit origin is mandatory once AllowCredentials is on (spec §11.5).
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            });
        });
    }

    /// <summary>
    /// A client that does not follow redirects, so a test asserting a 401 or a 429 sees
    /// exactly what the server returned.
    /// </summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });
}
