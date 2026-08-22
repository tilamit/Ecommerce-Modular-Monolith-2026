using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShopHub.Shared.Infrastructure.Correlation;

namespace ShopHub.IntegrationTests;

/// <summary>
/// The Phase 1 acceptance criteria (spec §14), asserted against the running pipeline:
/// health is green, the API document renders, a thrown exception produces a clean
/// ProblemDetails carrying a traceId, and hammering an endpoint returns 429.
/// </summary>
public sealed class CrossCuttingTests(ShopHubApiFactory factory) : IClassFixture<ShopHubApiFactory>
{
    [Fact]
    public async Task Liveness_IsHealthy()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_ReportsTheDatabaseProbe()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        // The probe must be present and named. Whether SQL Server is reachable from this
        // machine is a separate question, asserted by the run instructions in the README.
        var checks = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Contains(checks, c => c.GetProperty("name").GetString() == "sqlserver");
    }

    [Fact]
    public async Task OpenApiDocument_IsGenerated()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal("ShopHub API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnhandledException_ReturnsSanitizedProblemDetailsWithTraceId()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/_diagnostics/throw/unexpected", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal("internal_error", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("notfound", HttpStatusCode.NotFound, "not_found")]
    [InlineData("conflict", HttpStatusCode.Conflict, "conflict")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "forbidden")]
    [InlineData("domainrule", HttpStatusCode.UnprocessableContent, "domain_rule_violated")]
    public async Task DomainExceptions_MapToTheirDocumentedStatus(string kind, HttpStatusCode expected, string expectedCode)
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri($"/api/v1/_diagnostics/throw/{kind}", UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CorrelationId_IsEchoedBackToTheCaller()
    {
        using var client = factory.CreateApiClient();
        var correlationId = Guid.CreateVersion7().ToString("N");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(ShopHubHeaders.CorrelationId, correlationId);

        using var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues(ShopHubHeaders.CorrelationId, out var echoed));
        Assert.Equal(correlationId, echoed!.Single());
    }

    [Fact]
    public async Task CorrelationId_IsGeneratedWhenTheCallerSendsNone()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.True(response.Headers.TryGetValues(ShopHubHeaders.CorrelationId, out var generated));
        Assert.False(string.IsNullOrWhiteSpace(generated!.Single()));
    }

    /// <summary>
    /// Spec §14 Phase 1: "hammering an endpoint returns 429". The diagnostics endpoint
    /// carries the strictest policy (5 requests per minute), so the sixth call is rejected.
    /// </summary>
    [Fact]
    public async Task HammeringARateLimitedEndpoint_Returns429WithProblemDetails()
    {
        using var client = factory.CreateApiClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 8; i++)
        {
            using var response = await client.GetAsync(new Uri("/api/v1/_diagnostics/rate-limited", UriKind.Relative));
            statuses.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

                var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
                Assert.Equal("rate_limited", problem.GetProperty("code").GetString());
            }
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
