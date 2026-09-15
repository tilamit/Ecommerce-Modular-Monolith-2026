using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Spec §14 Phase 1: "hammering an endpoint returns 429".
/// <para>
/// Runs against its own host, because <see cref="ShopHubApiFactory"/> raises the
/// <c>auth</c> limit so that functional tests are not throttled by a limiter they are not
/// trying to exercise. Here the production limit of 5 per minute is left intact.
/// </para>
/// </summary>
public sealed class RateLimitingTests : IAsyncLifetime, IDisposable
{
    private readonly RateLimitedApiFactory _factory = new();

    public Task InitializeAsync() => _factory.StartAsync();

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    // The factory is disposed asynchronously above; this satisfies CA1001 for the field.
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task HammeringARateLimitedEndpoint_Returns429WithProblemDetails()
    {
        using var client = _factory.CreateApiClient();
        var statuses = new List<HttpStatusCode>();
        var sawProblemDetails = false;

        for (var i = 0; i < 8; i++)
        {
            using var response = await client.GetAsync(new Uri("/api/v1/_diagnostics/rate-limited", UriKind.Relative));
            statuses.Add(response.StatusCode);

            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                continue;
            }

            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("rate_limited", problem.GetProperty("code").GetString());
            sawProblemDetails = true;
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.True(sawProblemDetails);

        // The limit is 5/minute, so the first five succeed and the rest are rejected.
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.OK));
    }

    /// <summary>
    /// ADR-029: <c>/auth/me</c> is not in the login brute-force bucket. A sign-in plus a
    /// page reload spends five <c>auth</c> permits on its own and when <c>/me</c> shared
    /// them the SPA got 429 and rendered a customer's sidebar with no menu.
    /// </summary>
    [Fact]
    public async Task Me_IsNotThrottledByTheAuthPolicy()
    {
        using var client = _factory.CreateApiClient();

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", TestUsers.SecondCustomerLogin);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());

        for (var i = 0; i < 8; i++)
        {
            using var me = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);

            var body = await me.Content.ReadFromJsonAsync<JsonElement>();
            var routes = body.GetProperty("menu").EnumerateArray().Select(m => m.GetProperty("route").GetString());

            Assert.Contains("/account/orders", routes);
        }
    }
}
