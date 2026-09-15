using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Phase 3 acceptance (spec §14): browse, filter, sort and typeahead over the seeded
/// catalogue and - the headline criterion - "a product write invalidates the cached list
/// within one request".
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class CatalogTests(ShopHubApiFactory factory)
{
    [Fact]
    public async Task Storefront_ListsProductsAnonymously()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/catalog/products", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(page.GetProperty("totalCount").GetInt64() > 0);
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
    }

    /// <summary>
    /// A query-string value that will not bind is the caller's mistake. The failure happens
    /// in minimal-API parameter binding, upstream of every validator, so only the exception
    /// handler can classify it - and until it did, a hand-edited storefront URL answered
    /// 500 with a stack trace.
    /// </summary>
    [Fact]
    public async Task Storefront_RejectsAnUnbindableFilterWith400()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/catalog/products?minPrice=abc", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("malformed_request", problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// A request that joins another request's output-cache entry must not inherit its
    /// abandonment.
    /// <para>
    /// Output-cache request locking is on by default: concurrent callers asking for one
    /// cache key do not each execute the pipeline, one does and the rest are handed what it
    /// buffered. When that one's caller hangs up, what it buffered is nothing and every
    /// request behind it is answered <b>200 with an empty body</b> - no exception, nothing
    /// logged above Information. The SPA parsed that to <c>undefined</c> and threw while
    /// rendering, which is the error panel the storefront showed on a first run.
    /// </para>
    /// <para>
    /// Timing-dependent by nature, so this runs the race repeatedly. Against the unfixed
    /// endpoint it failed on the first or second iteration; the assertion itself is exact,
    /// so a passing run is never luck.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Storefront_DoesNotServeAnEmptyBodyToARequestThatJoinedACancelledOne()
    {
        using var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            // A cache key nothing has populated, so every iteration races from cold.
            var uri = new Uri(
                $"/api/v1/catalog/products?search=join{Guid.CreateVersion7():N}", UriKind.Relative);

            using var abandoned = new CancellationTokenSource();

            var owner = client.GetAsync(uri, abandoned.Token);
            var joiner = client.GetAsync(uri, CancellationToken.None);

            await abandoned.CancelAsync();

            try
            {
                using var ownerResponse = await owner;
            }
            catch (OperationCanceledException)
            {
                // The point of the exercise: this caller is gone.
            }

            using var response = await joiner;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Reading it as JSON is the assertion: an empty body is what the bug produced,
            // and `ReadFromJsonAsync` cannot parse one.
            var page = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(JsonValueKind.Array, page.GetProperty("items").ValueKind);
        }
    }

    // --- uploaded images -------------------------------------------------

    /// <summary>A real, minimal PNG. Small enough to inline and valid enough to be served back.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent ImageForm(params (string Name, byte[] Bytes, string ContentType)[] files)
    {
        var form = new MultipartFormDataContent();

        foreach (var (name, bytes, contentType) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, "files", name);
        }

        return form;
    }

    /// <summary>
    /// The whole point of the upload endpoint: a set of images is attached in one request
    /// rather than one round trip per file.
    /// </summary>
    [Fact]
    public async Task ImageUpload_StoresEveryFileInOneRequest()
    {
        using var client = await AdminClientAsync();

        using var form = ImageForm(("first.png", Png(), "image/png"), ("second.png", Png(), "image/png"));
        using var response = await client.PostAsync(new Uri("/api/v1/catalog/products/images", UriKind.Relative), form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, stored.GetArrayLength());

        foreach (var image in stored.EnumerateArray())
        {
            var url = image.GetProperty("url").GetString()!;

            // Served back anonymously, because the storefront renders these.
            using var anonymous = factory.CreateApiClient();
            using var fetched = await anonymous.GetAsync(new Uri(url, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
            Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        }
    }

    /// <summary>
    /// The file name is generated by the server and never taken from the upload, so a name
    /// that could walk out of the folder is not one this storage would ever have produced.
    /// A 404 rather than a 400: probing must not tell a rejected name from a missing file.
    /// </summary>
    [Theory]
    [InlineData("..%2F..%2Fappsettings.json")]
    [InlineData("not-a-stored-name.png")]
    [InlineData("01a0879067377189803.png")]
    public async Task ImageRead_RefusesAnythingItDidNotGenerate(string fileName)
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            new Uri($"/api/v1/catalog/products/images/{fileName}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ImageUpload_RejectsATypeThatIsNotAnImage()
    {
        using var client = await AdminClientAsync();

        using var form = ImageForm(("payload.sh", "#!/bin/sh"u8.ToArray(), "text/plain"));
        using var response = await client.PostAsync(new Uri("/api/v1/catalog/products/images", UriKind.Relative), form);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unsupported_image_type", problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// Hiding the button is not the boundary. Uploading is gated on the same permission the
    /// admin screen checks, enforced server-side.
    /// </summary>
    [Fact]
    public async Task ImageUpload_RequiresTheWritePermission()
    {
        using var client = factory.CreateApiClient();

        using var form = ImageForm(("a.png", Png(), "image/png"));
        using var response = await client.PostAsync(new Uri("/api/v1/catalog/products/images", UriKind.Relative), form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The round trip the admin form makes: create with a mix of an uploaded file and an
    /// external address, then edit down to one image and switch which is primary.
    /// </summary>
    [Fact]
    public async Task Product_RoundTripsItsImagesThroughCreateAndUpdate()
    {
        using var client = await AdminClientAsync();

        using var form = ImageForm(("a.png", Png(), "image/png"), ("b.png", Png(), "image/png"));
        using var upload = await client.PostAsync(new Uri("/api/v1/catalog/products/images", UriKind.Relative), form);
        var stored = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var urls = stored.EnumerateArray().Select(i => i.GetProperty("url").GetString()!).ToArray();

        var categoryId = await FirstLeafCategoryIdAsync(client);

        var sku = $"IMG-{Guid.CreateVersion7():N}"[..20];

        using var created = await client.PostAsJsonAsync(
            new Uri("/api/v1/catalog/products", UriKind.Relative),
            new
            {
                categoryId,
                sku,
                name = "Image round trip",
                price = 10m,
                currencyCode = "USD",
                stockQuantity = 1,
                isFeatured = false,
                images = new[]
                {
                    new { url = urls[0], altText = "uploaded", displayOrder = 0, isPrimary = true },
                    new { url = "https://example.com/external.jpg", altText = "external", displayOrder = 1, isPrimary = false },
                },
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = product.GetProperty("id").GetString();

        var detail = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/catalog/products/{id}", UriKind.Relative));

        // An uploaded file and an external address sit side by side: the column is a URL.
        Assert.Equal(2, detail.GetProperty("images").GetArrayLength());

        using var updated = await client.PutAsJsonAsync(
            new Uri($"/api/v1/catalog/products/{id}", UriKind.Relative),
            new
            {
                categoryId,
                sku,
                name = "Image round trip",
                price = 10m,
                currencyCode = "USD",
                stockQuantity = 1,
                isFeatured = false,
                images = new[]
                {
                    new { url = urls[1], altText = "now primary", displayOrder = 0, isPrimary = true },
                },
            });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/catalog/products/{id}", UriKind.Relative));

        var image = Assert.Single(after.GetProperty("images").EnumerateArray());
        Assert.Equal(urls[1], image.GetProperty("url").GetString());
        Assert.True(image.GetProperty("isPrimary").GetBoolean());

        using var deleted = await client.DeleteAsync(new Uri($"/api/v1/catalog/products/{id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    /// <summary>
    /// Anonymous callers must never see inactive products, even by asking for them -
    /// the endpoint overrides the filter rather than trusting it.
    /// </summary>
    [Fact]
    public async Task Storefront_NeverExposesInactiveProducts()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/catalog/products?isActive=false&pageSize=100", UriKind.Relative));

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.All(
            page.GetProperty("items").EnumerateArray(),
            item => Assert.True(item.GetProperty("isActive").GetBoolean()));
    }

    [Fact]
    public async Task CategoryTree_IsNestedAndAnonymous()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/catalog/categories/tree", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tree = await response.Content.ReadFromJsonAsync<JsonElement>();
        var roots = tree.EnumerateArray().ToArray();

        Assert.NotEmpty(roots);
        Assert.Contains(roots, r => r.GetProperty("children").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Typeahead_ReturnsPrefixMatchesCappedAtTen()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/catalog/products/suggest?q=Laptop", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var suggestions = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = suggestions.EnumerateArray().ToArray();

        Assert.NotEmpty(items);
        Assert.True(items.Length <= 10);
        Assert.All(items, i => Assert.StartsWith("Laptop", i.GetProperty("name").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Typeahead_WithAnEmptyTermReturnsNothing()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/catalog/products/suggest?q=", UriKind.Relative));

        var suggestions = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(suggestions.EnumerateArray());
    }

    [Fact]
    public async Task Sorting_IsWhitelistedAndDeterministic()
    {
        using var client = factory.CreateApiClient();

        using var ascending = await client.GetAsync(new Uri("/api/v1/catalog/products?sort=price&pageSize=20", UriKind.Relative));
        var page = await ascending.Content.ReadFromJsonAsync<JsonElement>();

        var prices = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("price").GetDecimal()).ToArray();

        Assert.Equal(prices.OrderBy(p => p).ToArray(), prices);
    }

    [Fact]
    public async Task AnUnknownSortField_FallsBackInsteadOfFailing()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/catalog/products?sort=' OR 1=1--", UriKind.Relative));

        // A client string never reaches OrderBy (spec §6.5); an unrecognised field is
        // ignored rather than surfaced as an error or interpolated into SQL.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PriceFilter_BoundsTheResults()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/catalog/products?minPrice=100&maxPrice=200&pageSize=100", UriKind.Relative));

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.All(
            page.GetProperty("items").EnumerateArray(),
            item =>
            {
                var price = item.GetProperty("price").GetDecimal();
                Assert.InRange(price, 100m, 200m);
            });
    }

    [Fact]
    public async Task ProductDetail_IsReachableByIdAndBySlug()
    {
        using var client = factory.CreateApiClient();

        using var listResponse = await client.GetAsync(new Uri("/api/v1/catalog/products?pageSize=1", UriKind.Relative));
        var first = (await listResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];

        var id = first.GetProperty("id").GetGuid();
        var slug = first.GetProperty("slug").GetString();

        using var byId = await client.GetAsync(new Uri($"/api/v1/catalog/products/{id}", UriKind.Relative));
        using var bySlug = await client.GetAsync(new Uri($"/api/v1/catalog/products/{slug}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bySlug.StatusCode);

        var fromId = await byId.Content.ReadFromJsonAsync<JsonElement>();
        var fromSlug = await bySlug.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(fromId.GetProperty("id").GetGuid(), fromSlug.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task UnknownProduct_Is404()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            new Uri($"/api/v1/catalog/products/{Guid.CreateVersion7()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The Phase 3 acceptance criterion. Two caches sit in front of this endpoint -
    /// HybridCache holds the query result and OutputCache holds the whole response body -
    /// so the read is repeated on the <em>same URL</em>, which is the only way to prove
    /// both layers were invalidated.
    /// </summary>
    [Fact]
    public async Task AProductWrite_InvalidatesTheCachedListWithinOneRequest()
    {
        const string Url = "/api/v1/catalog/products?pageSize=1&search=CacheProbe";

        using var anonymous = factory.CreateApiClient();
        using var admin = await AdminClientAsync();

        // Populate both cache layers for this exact URL.
        using var before = await anonymous.GetAsync(new Uri(Url, UriKind.Relative));
        var countBefore = (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt64();

        var categoryId = await FirstLeafCategoryIdAsync(anonymous);

        using var created = await admin.PostAsJsonAsync(
            "/api/v1/catalog/products",
            new
            {
                categoryId,
                sku = $"CACHEPROBE-{Guid.CreateVersion7():N}"[..24],
                name = $"CacheProbe {Guid.CreateVersion7():N}"[..24],
                price = 42.00m,
                currencyCode = "USD",
                stockQuantity = 5,
                isFeatured = false,
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var after = await anonymous.GetAsync(new Uri(Url, UriKind.Relative));
        var countAfter = (await after.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt64();

        Assert.Equal(countBefore + 1, countAfter);
    }

    [Fact]
    public async Task DeletingACategoryWithProducts_Is409UnlessReassigned()
    {
        using var admin = await AdminClientAsync();
        using var anonymous = factory.CreateApiClient();

        var categoryId = await FirstLeafCategoryIdAsync(anonymous);

        using var response = await admin.DeleteAsync(
            new Uri($"/api/v1/catalog/categories/{categoryId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("category_has_products", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreatingAProductWithADuplicateSku_Is409()
    {
        using var admin = await AdminClientAsync();
        using var anonymous = factory.CreateApiClient();

        var categoryId = await FirstLeafCategoryIdAsync(anonymous);

        using var listResponse = await anonymous.GetAsync(new Uri("/api/v1/catalog/products?pageSize=1", UriKind.Relative));
        var existingSku = (await listResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items")[0]
            .GetProperty("sku")
            .GetString();

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/catalog/products",
            new
            {
                categoryId,
                sku = existingSku,
                name = "Duplicate SKU probe",
                price = 10.00m,
                currencyCode = "USD",
                stockQuantity = 1,
                isFeatured = false,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sku_taken", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritingAProduct_RequiresThePermission()
    {
        using var customer = factory.CreateApiClient();

        using var login = await customer.PostAsJsonAsync("/api/v1/auth/login", TestUsers.CustomerLogin);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await customer.PostAsJsonAsync(
            "/api/v1/catalog/products",
            new
            {
                categoryId = Guid.CreateVersion7(),
                sku = "FORBIDDEN-1",
                name = "Should not be created",
                price = 1.00m,
                currencyCode = "USD",
                stockQuantity = 1,
                isFeatured = false,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NewArrivals_AreWithinTheLastThirtyDays()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/api/v1/catalog/products/new?pageSize=50", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cutoff = DateTime.UtcNow.AddDays(-31);

        Assert.All(
            page.GetProperty("items").EnumerateArray(),
            item => Assert.True(item.GetProperty("createdUtc").GetDateTime() >= cutoff));
    }

    // --- helpers ---------------------------------------------------------

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", TestUsers.AdminLogin);
        response.EnsureSuccessStatusCode();

        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<Guid> FirstLeafCategoryIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/catalog/categories/tree", UriKind.Relative));
        var tree = await response.Content.ReadFromJsonAsync<JsonElement>();

        return tree.EnumerateArray()
            .SelectMany(root => root.GetProperty("children").EnumerateArray())
            .First()
            .GetProperty("id")
            .GetGuid();
    }
}
