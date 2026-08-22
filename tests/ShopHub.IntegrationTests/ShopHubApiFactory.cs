using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Hosts the real API in-process against a <b>per-run database</b> (spec §12: "Integration
/// tests must use a per-run database and clean up. Do not share state between tests.")
/// <para>
/// Runs as Development, so the host applies each module's migrations and seeds on startup -
/// which means these tests exercise the real migration and seeding path, not a shortcut.
/// </para>
/// </summary>
public class ShopHubApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Seed passwords for this run. Supplied through configuration, never hard-coded in the app.</summary>
    internal const string AdminPassword = "Test!AdminPassw0rd";

    internal const string CustomerPassword = "Test!CustomerPassw0rd";

    private readonly string _databaseName = $"ShopHub_Test_{Guid.CreateVersion7():N}";

    private string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>
    /// Requests per minute allowed on the <c>auth</c> policy. Raised well above the
    /// production value of 5 so functional tests are not throttled by a limiter they are
    /// not trying to test. <see cref="RateLimitedApiFactory"/> keeps the real limit for the
    /// test that does.
    /// </summary>
    protected virtual int AuthRequestsPerMinute => 10_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ShopHub"] = ConnectionString,

                // An explicit origin is mandatory once AllowCredentials is on (spec §11.5).
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",

                // A test-only key. Real deployments read this from user-secrets or the
                // environment; the app refuses to start without one either way.
                ["Jwt:SigningKey"] = "integration-test-signing-key-not-used-anywhere-else-0123456789",

                ["Seed:AdminPassword"] = AdminPassword,
                ["Seed:CustomerPassword"] = CustomerPassword,

                ["RateLimiting:Auth:PermitLimit"] = AuthRequestsPerMinute.ToString(CultureInfo.InvariantCulture),
                ["RateLimiting:Authenticated:TokenLimit"] = "10000",
                ["RateLimiting:Write:PermitLimit"] = "10000",
            });
        });
    }

    /// <summary>
    /// A client that does not follow redirects and does <b>not</b> manage cookies.
    /// <para>
    /// Both defaults would corrupt these tests. Auto-redirect hides the status a test is
    /// asserting on; the cookie container silently replays whatever the server last set,
    /// so a test that deliberately presents an <em>old</em> refresh token would have the
    /// current one attached alongside it and the replay would appear to succeed. Cookies
    /// are set explicitly per request instead.
    /// </para>
    /// </summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    /// <summary>
    /// Forces host startup, which applies migrations and seeds the fresh database.
    /// <para>
    /// xUnit v2's <see cref="IAsyncLifetime"/> uses <see cref="Task"/>, and its
    /// <c>DisposeAsync</c> collides with <see cref="WebApplicationFactory{TEntryPoint}"/>'s
    /// <see cref="ValueTask"/> one - hence the explicit interface implementations.
    /// </para>
    /// </summary>
    public async Task StartAsync()
    {
        using var client = CreateApiClient();
        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.EnsureSuccessStatusCode();
    }

    Task IAsyncLifetime.InitializeAsync() => StartAsync();

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await DropDatabaseAsync();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Drops the run's database. SINGLE_USER WITH ROLLBACK IMMEDIATE first, because the
    /// connection pool may still hold idle connections that would otherwise block the drop.
    /// </summary>
    private async Task DropDatabaseAsync()
    {
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID('{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END
            """;

        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// The same host, but with the production <c>auth</c> rate limit intact, for the test that
/// asserts hammering an endpoint returns 429.
/// </summary>
public sealed class RateLimitedApiFactory : ShopHubApiFactory
{
    protected override int AuthRequestsPerMinute => 5;
}

/// <summary>
/// Shares one host and one database across the suite. Each run still gets its own database;
/// this only avoids paying migration and seeding cost once per test class.
/// </summary>
[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<ShopHubApiFactory>;
