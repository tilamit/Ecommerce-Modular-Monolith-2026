using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Every create, read, update and delete of a product, category, offer, profile and user role
/// assignment is recorded in the audit trail with the values that changed. Deletes are soft
/// and are recorded as deletes.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AuditTrailCoverageTests(ShopHubApiFactory factory)
{
    [Fact]
    public async Task AProductsLifecycle_IsRecordedIncludingReadsImagesAndTheSoftDelete()
    {
        using var admin = await ClientForAsync(TestUsers.AdminLogin);
        var categoryId = await FirstCategoryIdAsync(admin);
        var sku = $"AUD-{Guid.NewGuid():N}"[..20];

        using var create = await admin.PostAsJsonAsync("/api/v1/catalog/products", new
        {
            categoryId,
            sku,
            name = "Audit test lamp",
            price = 49.99m,
            currencyCode = "USD",
            stockQuantity = 5,
            isFeatured = false,
            images = new[] { new { url = "https://example.com/lamp.jpg", altText = "Lamp", displayOrder = 0, isPrimary = true } },
        });

        Assert.True(create.IsSuccessStatusCode, $"Creating the product returned {(int)create.StatusCode}.");
        var productId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var inserted = await AuditTrailProbe.WaitForAsync(admin, "Insert", "Product", productId);
        Assert.Equal("Audit test lamp", inserted.NewValues!.Value.GetProperty("Name").GetString());

        var images = await AuditTrailProbe.WaitForAsync(admin, "Update", "Product", productId, match: e => e.Changed.Contains("Images"));
        Assert.Empty(images.OldList("Images"));
        Assert.Equal(new[] { "https://example.com/lamp.jpg (primary)" }, images.NewList("Images"));

        using (var read = await admin.GetAsync(new Uri($"/api/v1/catalog/products/{productId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        await AuditTrailProbe.WaitForAsync(admin, "Read", "Product", productId);

        using var update = await admin.PutAsJsonAsync($"/api/v1/catalog/products/{productId}", new
        {
            categoryId,
            sku,
            name = "Audit test lamp",
            price = 59.99m,
            currencyCode = "USD",
            stockQuantity = 5,
            isFeatured = false,
            images = new[]
            {
                new { url = "https://example.com/lamp.jpg", altText = "Lamp", displayOrder = 0, isPrimary = false },
                new { url = "https://example.com/lamp-side.jpg", altText = "Lamp side", displayOrder = 1, isPrimary = true },
            },
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var priceChange = await AuditTrailProbe.WaitForAsync(admin, "Update", "Product", productId, match: e => e.Changed.Contains("Price"));
        Assert.Equal(49.99m, priceChange.OldValues!.Value.GetProperty("Price").GetDecimal());
        Assert.Equal(59.99m, priceChange.NewValues!.Value.GetProperty("Price").GetDecimal());

        var imageChange = await AuditTrailProbe.WaitForAsync(
            admin,
            "Update",
            "Product",
            productId,
            afterId: images.Id,
            match: e => e.Changed.Contains("Images"));

        Assert.Equal(new[] { "https://example.com/lamp.jpg (primary)" }, imageChange.OldList("Images"));
        Assert.Equal(
            new[] { "https://example.com/lamp.jpg", "https://example.com/lamp-side.jpg (primary)" },
            imageChange.NewList("Images"));

        using (var delete = await admin.DeleteAsync(new Uri($"/api/v1/catalog/products/{productId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        var deleted = await AuditTrailProbe.WaitForAsync(admin, "Delete", "Product", productId);
        Assert.True(deleted.NewValues!.Value.GetProperty("IsDeleted").GetBoolean());

        using var afterDelete = await admin.GetAsync(new Uri($"/api/v1/catalog/products/{productId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task ACategorysLifecycle_IsRecordedIncludingTheReadAndTheSoftDelete()
    {
        using var admin = await ClientForAsync(TestUsers.AdminLogin);
        var name = $"Audit category {Guid.NewGuid():N}"[..30];

        using var create = await admin.PostAsJsonAsync("/api/v1/catalog/categories", new { name, displayOrder = 900 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var categoryId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await AuditTrailProbe.WaitForAsync(admin, "Insert", "Category", categoryId);

        using (var read = await admin.GetAsync(new Uri($"/api/v1/catalog/categories/{categoryId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(name, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
        }

        await AuditTrailProbe.WaitForAsync(admin, "Read", "Category", categoryId);

        using (var update = await admin.PutAsJsonAsync($"/api/v1/catalog/categories/{categoryId}", new { name, displayOrder = 901 }))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        var updated = await AuditTrailProbe.WaitForAsync(admin, "Update", "Category", categoryId, match: e => e.Changed.Contains("DisplayOrder"));
        Assert.Equal(900, updated.OldValues!.Value.GetProperty("DisplayOrder").GetInt32());
        Assert.Equal(901, updated.NewValues!.Value.GetProperty("DisplayOrder").GetInt32());

        using (var delete = await admin.DeleteAsync(new Uri($"/api/v1/catalog/categories/{categoryId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        await AuditTrailProbe.WaitForAsync(admin, "Delete", "Category", categoryId);

        using var afterDelete = await admin.GetAsync(new Uri($"/api/v1/catalog/categories/{categoryId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task AnOffersLifecycle_IsRecordedAndDeletingItIsSoftAndFreesTheCode()
    {
        using var admin = await ClientForAsync(TestUsers.AdminLogin);
        var code = $"AUD{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        object Offer(decimal value, bool isActive = true) => new
        {
            code,
            name = "Audit test offer",
            discountType = "Percentage",
            discountValue = value,
            startUtc = DateTime.UtcNow.AddDays(-1),
            endUtc = DateTime.UtcNow.AddDays(10),
            isActive,
        };

        using var create = await admin.PostAsJsonAsync("/api/v1/catalog/offers", Offer(10m));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var offerId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await AuditTrailProbe.WaitForAsync(admin, "Insert", "Offer", offerId);

        using (var read = await admin.GetAsync(new Uri($"/api/v1/catalog/offers/{offerId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        await AuditTrailProbe.WaitForAsync(admin, "Read", "Offer", offerId);

        using (var update = await admin.PutAsJsonAsync($"/api/v1/catalog/offers/{offerId}", Offer(15m)))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        var updated = await AuditTrailProbe.WaitForAsync(admin, "Update", "Offer", offerId, match: e => e.Changed.Contains("DiscountValue"));
        Assert.Equal(10m, updated.OldValues!.Value.GetProperty("DiscountValue").GetDecimal());
        Assert.Equal(15m, updated.NewValues!.Value.GetProperty("DiscountValue").GetDecimal());

        using (var delete = await admin.DeleteAsync(new Uri($"/api/v1/catalog/offers/{offerId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        await AuditTrailProbe.WaitForAsync(admin, "Delete", "Offer", offerId);

        using (var afterDelete = await admin.GetAsync(new Uri($"/api/v1/catalog/offers/{offerId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        }

        // The deleted offer keeps its row but no longer holds the code.
        using var reuse = await admin.PostAsJsonAsync("/api/v1/catalog/offers", Offer(5m));
        Assert.Equal(HttpStatusCode.Created, reuse.StatusCode);
    }

    [Fact]
    public async Task ReadingAndUpdatingAProfile_IsRecordedForTheSignedInUser()
    {
        using var admin = await ClientForAsync(TestUsers.AdminLogin);
        var (userId, email, password) = await CreateCustomerAsync(admin);

        using var customer = await ClientForAsync(new { email, password });

        using (var read = await customer.GetAsync(new Uri("/api/v1/users/me/profile", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        var readEntry = await AuditTrailProbe.WaitForAsync(admin, "Read", "User", userId);
        Assert.Equal(userId.ToString(), readEntry.UserId);

        using (var update = await customer.PutAsJsonAsync(
            "/api/v1/users/me/profile",
            new { firstName = "Renamed", lastName = "Customer", phoneNumber = "+1 555 0199" }))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        var updated = await AuditTrailProbe.WaitForAsync(admin, "Update", "User", userId, match: e => e.Changed.Contains("FirstName"));
        Assert.Equal("Audit", updated.OldValues!.Value.GetProperty("FirstName").GetString());
        Assert.Equal("Renamed", updated.NewValues!.Value.GetProperty("FirstName").GetString());
    }

    [Fact]
    public async Task ChangingAUsersRole_RecordsTheRoleNamesBeforeAndAfter()
    {
        using var admin = await ClientForAsync(TestUsers.AdminLogin);
        var (userId, email, _) = await CreateCustomerAsync(admin);

        var created = await AuditTrailProbe.WaitForAsync(admin, "PermissionChange", "User", userId);
        Assert.Empty(created.OldList("Roles"));
        Assert.Equal(new[] { "Customer" }, created.NewList("Roles"));
        Assert.Contains(email, created.NewValues!.Value.GetProperty("User").GetString(), StringComparison.Ordinal);

        var adminRoleId = await RoleIdAsync(admin, "Admin");

        using (var change = await admin.PutAsJsonAsync($"/api/v1/users/{userId}/roles", new { roleIds = new[] { adminRoleId } }))
        {
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }

        var changed = await AuditTrailProbe.WaitForAsync(admin, "PermissionChange", "User", userId, afterId: created.Id);
        Assert.Equal(new[] { "Roles" }, changed.Changed);
        Assert.Equal(new[] { "Customer" }, changed.OldList("Roles"));
        Assert.Equal(new[] { "Admin" }, changed.NewList("Roles"));
    }

    // --- helpers ------------------------------------------------------------

    private static async Task<(Guid Id, string Email, string Password)> CreateCustomerAsync(HttpClient admin)
    {
        var email = $"audit-{Guid.NewGuid():N}@example.com";
        const string password = "Audit!Passw0rd";
        var customerRoleId = await RoleIdAsync(admin, "Customer");

        using var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email,
            password,
            firstName = "Audit",
            lastName = "Customer",
            roleIds = new[] { customerRoleId },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), email, password);
    }

    private static async Task<Guid> RoleIdAsync(HttpClient admin, string name)
    {
        using var response = await admin.GetAsync(new Uri("/api/v1/roles?pageSize=100", UriKind.Relative));
        var roles = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");

        return roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> FirstCategoryIdAsync(HttpClient admin)
    {
        using var response = await admin.GetAsync(new Uri("/api/v1/catalog/categories?pageSize=100", UriKind.Relative));
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");

        return items.EnumerateArray().First(c => c.TryGetProperty("parentId", out var p) && p.ValueKind == JsonValueKind.String)
            .GetProperty("id")
            .GetGuid();
    }

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
