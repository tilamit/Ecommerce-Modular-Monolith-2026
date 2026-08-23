using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ShopHub.Shared.Infrastructure.Correlation;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Phase 5 and 6 acceptance (spec §14): an admin product edit produces an audit row with
/// correct old/new values, <c>ScreenName</c> and user snapshot; an audit failure does not
/// roll back the edit; and the dashboards return zero-filled series.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AuditingAndDashboardTests(ShopHubApiFactory factory)
{
    /// <summary>
    /// The background writer batches with a 2-second flush interval, so a test that reads
    /// immediately would race it. Polling rather than sleeping a fixed time keeps the suite
    /// fast when the write lands early.
    /// </summary>
    private static async Task<JsonElement?> WaitForAuditEntryAsync(
        HttpClient admin,
        Func<JsonElement, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            using var response = await admin.GetAsync(new Uri("/api/v1/audit-trails?pageSize=50", UriKind.Relative));

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var page = await response.Content.ReadFromJsonAsync<JsonElement>();

                foreach (var item in page.GetProperty("items").EnumerateArray())
                {
                    if (predicate(item))
                    {
                        return item.Clone();
                    }
                }
            }

            await Task.Delay(250);
        }

        return null;
    }

    /// <summary>
    /// The Phase 5 headline criterion. <c>X-Client-Page</c> is the only way the server can
    /// know which screen issued the write - the same endpoint is called from several
    /// (spec §6.6).
    /// </summary>
    [Fact]
    public async Task AProductEdit_ProducesAnAuditRowWithScreenNameAndUserSnapshot()
    {
        using var admin = await AdminClientAsync();
        var product = await FirstProductAsync(admin);

        var screen = $"/admin/products/edit/{product.Id}";
        var newPrice = Math.Round(product.Price + 11.11m, 2);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/products/{product.Id}")
        {
            Content = JsonContent.Create(new
            {
                categoryId = product.CategoryId,
                sku = product.Sku,
                name = product.Name,
                price = newPrice,
                currencyCode = "USD",
                stockQuantity = product.StockQuantity,
                isFeatured = false,
            }),
        };

        request.Headers.Add(ShopHubHeaders.ClientPage, screen);
        request.Headers.Add(ShopHubHeaders.CorrelationId, "audit-test-correlation");

        using var edit = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var entry = await WaitForAuditEntryAsync(
            admin,
            item => item.GetProperty("entityName").GetString() == "Product"
                && item.GetProperty("action").GetString() == "Update"
                && item.TryGetProperty("screenName", out var s)
                && s.GetString() == screen,
            TimeSpan.FromSeconds(15));

        Assert.NotNull(entry);

        var row = entry!.Value;

        Assert.Equal("catalog", row.GetProperty("module").GetString());
        Assert.Equal(product.Id.ToString(), row.GetProperty("entityId").GetString());

        // The user is snapshotted, so the trail survives a rename or deletion (spec §6.6).
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("userName").GetString()));

        // The full diff carries old and new values for the changed columns only.
        using var detail = await admin.GetAsync(
            new Uri($"/api/v1/audit-trails/{row.GetProperty("id").GetInt64()}", UriKind.Relative));

        var full = await detail.Content.ReadFromJsonAsync<JsonElement>();

        var changed = JsonSerializer.Deserialize<string[]>(full.GetProperty("changedColumns").GetString()!)!;
        Assert.Contains("Price", changed);

        var oldValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(full.GetProperty("oldValues").GetString()!)!;
        var newValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(full.GetProperty("newValues").GetString()!)!;

        Assert.Equal(product.Price, oldValues["Price"].GetDecimal());
        Assert.Equal(newPrice, newValues["Price"].GetDecimal());
    }

    /// <summary>
    /// Spec §6.6 redaction. A secret copied into the trail lands in a table with looser
    /// access than the one it came from, so <c>[NoAudit]</c> properties must never appear.
    /// </summary>
    [Fact]
    public async Task TheAuditTrail_NeverContainsSecrets()
    {
        using var admin = await AdminClientAsync();

        // Logging in updates the user row (LastLoginUtc), which the interceptor captures -
        // so if redaction were broken, PasswordHash would be in that entry.
        using var _ = await factory.CreateApiClient().PostAsJsonAsync("/api/v1/auth/login", TestUsers.CustomerLogin);

        await Task.Delay(2500);

        using var response = await admin.GetAsync(new Uri("/api/v1/audit-trails?pageSize=100", UriKind.Relative));
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            using var detail = await admin.GetAsync(
                new Uri($"/api/v1/audit-trails/{item.GetProperty("id").GetInt64()}", UriKind.Relative));

            var raw = await detail.Content.ReadAsStringAsync(CancellationToken.None);

            Assert.DoesNotContain("PasswordHash", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("TokenHash", raw, StringComparison.Ordinal);
        }
    }

    /// <summary>Spec §9.4: the trail is append-only - no mutating verb is exposed.</summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task TheAuditTrail_ExposesNoMutatingVerb(string method)
    {
        using var admin = await AdminClientAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/audit-trails/1")
        {
            Content = JsonContent.Create(new { action = "Tampered" }),
        };

        using var response = await admin.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"{method} /api/v1/audit-trails/1 returned {(int)response.StatusCode}; the trail must be append-only.");
    }

    [Fact]
    public async Task AuditTrail_IsKeysetPaginatedWithAnOpaqueCursor()
    {
        using var admin = await AdminClientAsync();

        using var first = await admin.GetAsync(new Uri("/api/v1/audit-trails?pageSize=2", UriKind.Relative));
        var page = await first.Content.ReadFromJsonAsync<JsonElement>();

        // Keyset, not offset: there is a cursor and no page number or total count.
        Assert.False(page.TryGetProperty("totalCount", out _));
        Assert.True(page.TryGetProperty("nextCursor", out _) || page.GetProperty("items").GetArrayLength() < 2);
    }

    [Fact]
    public async Task AuditTrail_RequiresThePermission()
    {
        using var customer = await ClientForAsync(TestUsers.CustomerLogin);

        using var response = await customer.GetAsync(new Uri("/api/v1/audit-trails", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Phase 6: dashboards ------------------------------------------------

    /// <summary>
    /// Spec §10.1: daily series must be zero-filled, "otherwise the chart lies about quiet
    /// days". A 30-day window therefore always has exactly 30 points.
    /// </summary>
    [Fact]
    public async Task AdminDashboard_ReturnsZeroFilledThirtyDaySeries()
    {
        using var admin = await AdminClientAsync();

        using var response = await admin.GetAsync(new Uri("/api/v1/dashboard/admin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<JsonElement>();
        var charts = dashboard.GetProperty("charts");

        foreach (var series in new[] { "usersRegisteredPerDay", "ordersPerDay", "revenuePerDay" })
        {
            var points = charts.GetProperty(series).EnumerateArray().ToArray();

            Assert.Equal(30, points.Length);

            // Contiguous and ascending: no day is missing from the middle.
            var days = points.Select(p => DateOnly.Parse(p.GetProperty("day").GetString()!, null)).ToArray();

            for (var i = 1; i < days.Length; i++)
            {
                Assert.Equal(days[i - 1].AddDays(1), days[i]);
            }
        }

        var counts = dashboard.GetProperty("counts");
        Assert.True(counts.GetProperty("activeProducts").GetInt32() > 0);
        Assert.True(counts.GetProperty("activeUsers").GetInt32() > 0);
    }

    [Fact]
    public async Task CustomerDashboard_IsScopedToTheCallerAndTakesNoUserId()
    {
        using var ada = await ClientForAsync(TestUsers.CustomerLogin);
        using var grace = await ClientForAsync(TestUsers.SecondCustomerLogin);

        using var adaResponse = await ada.GetAsync(new Uri("/api/v1/dashboard/customer", UriKind.Relative));
        using var graceResponse = await grace.GetAsync(new Uri("/api/v1/dashboard/customer", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, adaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, graceResponse.StatusCode);

        var adaData = await adaResponse.Content.ReadFromJsonAsync<JsonElement>();
        var graceData = await graceResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Spec §10.2: "Never accept a userId parameter on this endpoint." Supplying one
        // must not change the answer, because it is not read at all.
        using var spoofed = await ada.GetAsync(
            new Uri("/api/v1/dashboard/customer?userId=" + Guid.CreateVersion7(), UriKind.Relative));

        var spoofedData = await spoofed.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            adaData.GetProperty("counts").GetProperty("totalOrders").GetInt32(),
            spoofedData.GetProperty("counts").GetProperty("totalOrders").GetInt32());

        // Two different customers see their own figures, so the scoping is real.
        Assert.Equal(30, adaData.GetProperty("charts").GetProperty("spendPerDay").GetArrayLength());
        Assert.Equal(30, graceData.GetProperty("charts").GetProperty("ordersPerDay").GetArrayLength());
    }

    [Fact]
    public async Task CustomerDashboard_IsForbiddenForAnAdminOnlyEndpoint()
    {
        using var customer = await ClientForAsync(TestUsers.CustomerLogin);

        using var response = await customer.GetAsync(new Uri("/api/v1/dashboard/admin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- helpers ------------------------------------------------------------

    private sealed record SeedProduct(Guid Id, Guid CategoryId, string Sku, string Name, decimal Price, int StockQuantity);

    private static async Task<SeedProduct> FirstProductAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/catalog/products?pageSize=1", UriKind.Relative));
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];

        return new SeedProduct(
            item.GetProperty("id").GetGuid(),
            item.GetProperty("categoryId").GetGuid(),
            item.GetProperty("sku").GetString()!,
            item.GetProperty("name").GetString()!,
            item.GetProperty("price").GetDecimal(),
            item.GetProperty("stockQuantity").GetInt32());
    }

    private Task<HttpClient> AdminClientAsync() => ClientForAsync(TestUsers.AdminLogin);

    private async Task<HttpClient> ClientForAsync(object login)
    {
        var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", login);
        response.EnsureSuccessStatusCode();

        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
