using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests;

/// <summary>
/// The Phase 2 acceptance criteria (spec §14): "the §12 integration tests for the full
/// token lifecycle pass, including reuse detection revoking a family."
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AuthLifecycleTests(ShopHubApiFactory factory)
{
    [Fact]
    public async Task Login_WithSeededAdmin_ReturnsAccessTokenAndRefreshCookie()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", TestUsers.AdminLogin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.True(body.TryGetProperty("accessTokenExpiresUtc", out _));
        Assert.True(body.TryGetProperty("refreshTokenExpiresUtc", out _));
        Assert.False(string.IsNullOrWhiteSpace(RefreshCookie(response)));
    }

    /// <summary>
    /// Spec A1 and ADR-004: the refresh token must never be
    /// reachable from JavaScript. The strongest form of that guarantee is that its value
    /// never appears in a response body at all.
    /// </summary>
    [Fact]
    public async Task Login_DoesNotPutTheRefreshTokenValueInTheBody()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", TestUsers.AdminLogin);

        var cookieValue = RefreshCookie(response);
        var rawBody = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.DoesNotContain(cookieValue, rawBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshCookie_IsHttpOnlyAndScopedToTheAuthPath()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", TestUsers.AdminLogin);

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("shophub_rt=", StringComparison.Ordinal));

        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsIndistinguishableFromUnknownEmail()
    {
        using var client = factory.CreateApiClient();

        using var wrongPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = TestUsers.AdminEmail, password = "definitely-not-the-password" });

        using var unknownEmail = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "nobody@shophub.local", password = "definitely-not-the-password" });

        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);

        var first = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>();
        var second = await unknownEmail.Content.ReadFromJsonAsync<JsonElement>();

        // Same code and same message: anything that differs is an enumeration oracle.
        Assert.Equal(first.GetProperty("code").GetString(), second.GetProperty("code").GetString());
        Assert.Equal(first.GetProperty("detail").GetString(), second.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Refresh_RotatesTheToken()
    {
        using var client = factory.CreateApiClient();

        var first = await LoginAsync(client);
        var rotated = await RefreshAsync(client, first.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(first.AccessToken, rotated.AccessToken);
    }

    /// <summary>
    /// Spec §7.3: refresh failure must be <b>401</b> with the distinguishable code
    /// <c>refresh_expired</c> - not 403 - so the SPA redirects to login instead of looping.
    /// </summary>
    [Fact]
    public async Task Refresh_WithNoCookie_Returns401RefreshExpired()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("refresh_expired", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401RefreshExpired()
    {
        using var client = factory.CreateApiClient();

        using var response = await RefreshRawAsync(client, "not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("refresh_expired", problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// Spec §7.2 reuse detection - the headline Phase 2 acceptance criterion.
    /// <para>
    /// Replaying an already-rotated token is the signature of theft, so the whole family
    /// dies. The critical assertion is the second one: the <em>legitimate</em> replacement
    /// token must also stop working, or an attacker who triggered the alarm still holds a
    /// usable credential.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Refresh_ReplayingARotatedToken_RevokesTheEntireFamily()
    {
        using var client = factory.CreateApiClient();

        var first = await LoginAsync(client);
        var second = await RefreshAsync(client, first.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Replay the token that was already rotated away.
        using var replay = await RefreshRawAsync(client, first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var problem = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("refresh_expired", problem.GetProperty("code").GetString());

        // The legitimate successor must now be dead too.
        using var successor = await RefreshRawAsync(client, second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        using var client = factory.CreateApiClient();

        var session = await LoginAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Cookie", $"shophub_rt={session.RefreshToken}");
        using var logout = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var afterLogout = await RefreshRawAsync(client, session.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsProfileRolesAndMenuTree()
    {
        using var client = factory.CreateApiClient();

        var session = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var profile = body.GetProperty("profile");
        var menu = body.GetProperty("menu");

        Assert.Equal(TestUsers.AdminEmail, profile.GetProperty("email").GetString());
        Assert.Contains("Admin", profile.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.NotEmpty(menu.EnumerateArray());
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutAToken_Is401()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithADuplicateEmail_Is409()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email = TestUsers.AdminEmail,
                password = "SomeOther!Passw0rd",
                firstName = "Duplicate",
                lastName = "Account",
                phoneNumber = (string?)null,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("email_taken", problem.GetProperty("code").GetString());
    }

    // --- helpers ---------------------------------------------------------

    private static async Task<Session> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", TestUsers.AdminLogin);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new Session(body.GetProperty("accessToken").GetString()!, RefreshCookie(response));
    }

    private static async Task<Rotation> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var response = await RefreshRawAsync(client, refreshToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new Rotation(response.StatusCode, string.Empty, string.Empty);
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new Rotation(
            response.StatusCode,
            body.GetProperty("accessToken").GetString()!,
            RefreshCookie(response));
    }

    private static async Task<HttpResponseMessage> RefreshRawAsync(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"shophub_rt={refreshToken}");

        return await client.SendAsync(request);
    }

    private static string RefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return string.Empty;
        }

        var cookie = cookies.FirstOrDefault(c => c.StartsWith("shophub_rt=", StringComparison.Ordinal));

        if (cookie is null)
        {
            return string.Empty;
        }

        var value = cookie["shophub_rt=".Length..];
        var semicolon = value.IndexOf(';', StringComparison.Ordinal);

        return semicolon >= 0 ? value[..semicolon] : value;
    }

    private sealed record Session(string AccessToken, string RefreshToken);

    private sealed record Rotation(HttpStatusCode StatusCode, string AccessToken, string RefreshToken);
}
