using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Spec §7.5: endpoints require a <em>permission</em>, and client-side hiding is UX, not
/// security - "every endpoint enforces its own permission".
/// <para>
/// These assert the server actually refuses, rather than trusting that the SPA will not
/// render the button.
/// </para>
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class PermissionEnforcementTests(ShopHubApiFactory factory)
{
    [Fact]
    public async Task Customer_CannotListUsers()
    {
        using var client = await AuthenticatedClientAsync(TestUsers.CustomerLogin);

        using var response = await client.GetAsync(new Uri("/api/v1/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotReadTheMenuTree()
    {
        using var client = await AuthenticatedClientAsync(TestUsers.CustomerLogin);

        using var response = await client.GetAsync(new Uri("/api/v1/menus", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotCreateAUser()
    {
        using var client = await AuthenticatedClientAsync(TestUsers.CustomerLogin);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email = "intruder@example.com",
                password = "Intruder!Passw0rd",
                firstName = "In",
                lastName = "Truder",
                phoneNumber = (string?)null,
                roleIds = Array.Empty<Guid>(),
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanListUsers()
    {
        using var client = await AuthenticatedClientAsync(TestUsers.AdminLogin);

        using var response = await client.GetAsync(new Uri("/api/v1/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(page.GetProperty("totalCount").GetInt64() >= 4);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task AnonymousCaller_Gets401NotForbidden()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/users", UriKind.Relative));

        // 401 means "authenticate and retry"; 403 would tell an anonymous caller the
        // resource exists and that they are permanently barred. The distinction matters
        // to the SPA, which redirects to login on 401 only.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A customer's own profile is reachable without any admin permission, and is scoped
    /// server-side - the endpoint takes no id at all (spec §7.5).
    /// </summary>
    [Fact]
    public async Task Customer_CanReadTheirOwnProfile()
    {
        using var client = await AuthenticatedClientAsync(TestUsers.CustomerLogin);

        using var response = await client.GetAsync(new Uri("/api/v1/users/me/profile", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TestUsers.CustomerEmail, profile.GetProperty("email").GetString());
    }

    /// <summary>
    /// The cross-account attempt: customer B's id supplied by customer A. The route needs
    /// an admin permission, so it must be refused regardless of whose id is in the URL.
    /// </summary>
    [Fact]
    public async Task Customer_CannotReadAnotherUserById()
    {
        using var admin = await AuthenticatedClientAsync(TestUsers.AdminLogin);
        using var listResponse = await admin.GetAsync(new Uri("/api/v1/users?pageSize=100", UriKind.Relative));
        var page = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        var otherUserId = page.GetProperty("items")
            .EnumerateArray()
            .First(u => u.GetProperty("email").GetString() == TestUsers.SecondCustomerEmail)
            .GetProperty("id")
            .GetGuid();

        using var customer = await AuthenticatedClientAsync(TestUsers.CustomerLogin);
        using var response = await customer.GetAsync(new Uri($"/api/v1/users/{otherUserId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pagination_ClampsAnOversizedPageSize()
    {
        using var client = await AuthenticatedClientAsync(TestUsers.AdminLogin);

        using var response = await client.GetAsync(new Uri("/api/v1/users?page=0&pageSize=10000", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Spec §6.5: pageSize clamps to [1,100] and page to >= 1, in one shared place.
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(100, page.GetProperty("pageSize").GetInt32());
    }

    private async Task<HttpClient> AuthenticatedClientAsync(object login)
    {
        var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", login);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
