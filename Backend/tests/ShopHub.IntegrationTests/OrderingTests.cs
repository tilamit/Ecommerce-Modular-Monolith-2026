using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Phase 4 acceptance (spec §14): "all four cart states in §8.4 are covered by tests,
/// including the idempotent merge and the guest-email-matches-user prompt."
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class OrderingTests(ShopHubApiFactory factory)
{
    // --- State 1: anonymous, browsing --------------------------------------

    /// <summary>
    /// Spec §8.4 state 1. The anonymous cart lives in localStorage and stores only
    /// product ids and quantities; the server re-prices it and persists nothing.
    /// </summary>
    [Fact]
    public async Task State1_AnonymousCartIsRepricedAndNotPersisted()
    {
        using var client = factory.CreateApiClient();
        var product = await FirstInStockProductAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/carts/price",
            new { items = new[] { new { productId = product.Id, quantity = 2 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cart = await response.Content.ReadFromJsonAsync<JsonElement>();

        // No cart id: nothing was written.
        Assert.False(cart.TryGetProperty("cartId", out var cartId) && cartId.ValueKind is not JsonValueKind.Null);

        // The price came from the catalogue, not from the request.
        Assert.Equal(product.Price * 2, cart.GetProperty("subTotal").GetDecimal());
    }

    /// <summary>
    /// The price-tampering guard. The endpoint accepts only product id and quantity, so a
    /// client has no field in which to send a price of its own choosing.
    /// </summary>
    [Fact]
    public async Task State1_ClientSuppliedPricesAreIgnored()
    {
        using var client = factory.CreateApiClient();
        var product = await FirstInStockProductAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/carts/price",
            new { items = new[] { new { productId = product.Id, quantity = 1, unitPrice = 0.01m, lineTotal = 0.01m } } });

        var cart = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(product.Price, cart.GetProperty("subTotal").GetDecimal());
    }

    // --- State 2: logged in, adding to cart --------------------------------

    [Fact]
    public async Task State2_LoggedInCartPersistsServerSide()
    {
        using var client = await CustomerClientAsync();
        var product = await FirstInStockProductAsync(client);

        await ClearCartAsync(client);

        using var added = await client.PostAsJsonAsync(
            "/api/v1/carts/me/items",
            new { productId = product.Id, quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        // Read it back on a fresh request: it is stored, not held in a session.
        using var fetched = await client.GetAsync(new Uri("/api/v1/carts/me", UriKind.Relative));
        var cart = await fetched.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, cart.GetProperty("totalQuantity").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, cart.GetProperty("cartId").ValueKind);
    }

    [Fact]
    public async Task State2_RemovingAnItemEmptiesTheCart()
    {
        using var client = await CustomerClientAsync();
        var product = await FirstInStockProductAsync(client);

        await ClearCartAsync(client);
        await client.PostAsJsonAsync("/api/v1/carts/me/items", new { productId = product.Id, quantity = 1 });

        using var removed = await client.DeleteAsync(
            new Uri($"/api/v1/carts/me/items/{product.Id}", UriKind.Relative));

        var cart = await removed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, cart.GetProperty("totalQuantity").GetInt32());
    }

    // --- State 3: merge on login -------------------------------------------

    [Fact]
    public async Task State3_MergeSumsQuantitiesIntoTheServerCart()
    {
        using var client = await CustomerClientAsync();
        var products = await InStockProductsAsync(client, 2);

        await ClearCartAsync(client);
        await client.PostAsJsonAsync("/api/v1/carts/me/items", new { productId = products[0].Id, quantity = 2 });

        using var merged = await client.PostAsJsonAsync(
            "/api/v1/carts/merge",
            new
            {
                mergeToken = Guid.CreateVersion7(),
                items = new[]
                {
                    new { productId = products[0].Id, quantity = 3 },
                    new { productId = products[1].Id, quantity = 1 },
                },
            });

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);

        var cart = await merged.Content.ReadFromJsonAsync<JsonElement>();

        // 2 already there + 3 merged + 1 new = 6.
        Assert.Equal(6, cart.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(2, cart.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// The headline Phase 4 criterion. A retry after a network blip must not double
    /// quantities (spec §8.4), so the same <c>mergeToken</c> is ignored the second time.
    /// </summary>
    [Fact]
    public async Task State3_ReplayingAMergeTokenDoesNotDoubleQuantities()
    {
        using var client = await CustomerClientAsync();
        var product = await FirstInStockProductAsync(client);

        await ClearCartAsync(client);

        var token = Guid.CreateVersion7();
        var payload = new { mergeToken = token, items = new[] { new { productId = product.Id, quantity = 3 } } };

        using var first = await client.PostAsJsonAsync("/api/v1/carts/merge", payload);
        var afterFirst = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalQuantity").GetInt32();

        using var replay = await client.PostAsJsonAsync("/api/v1/carts/merge", payload);
        var afterReplay = (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalQuantity").GetInt32();

        Assert.Equal(3, afterFirst);
        Assert.Equal(3, afterReplay);
    }

    [Fact]
    public async Task State3_ADifferentMergeTokenAppliesAgain()
    {
        using var client = await CustomerClientAsync();
        var product = await FirstInStockProductAsync(client);

        await ClearCartAsync(client);

        await client.PostAsJsonAsync(
            "/api/v1/carts/merge",
            new { mergeToken = Guid.CreateVersion7(), items = new[] { new { productId = product.Id, quantity = 2 } } });

        using var second = await client.PostAsJsonAsync(
            "/api/v1/carts/merge",
            new { mergeToken = Guid.CreateVersion7(), items = new[] { new { productId = product.Id, quantity = 2 } } });

        var cart = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, cart.GetProperty("totalQuantity").GetInt32());
    }

    /// <summary>A merge without a token is rejected - it could not be made idempotent.</summary>
    [Fact]
    public async Task State3_MergeWithoutATokenIs400()
    {
        using var client = await CustomerClientAsync();
        var product = await FirstInStockProductAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/carts/merge",
            new { mergeToken = Guid.Empty, items = new[] { new { productId = product.Id, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- State 4: guest checkout -------------------------------------------

    [Fact]
    public async Task State4_GuestCheckoutCreatesAnOrderAndAProfile()
    {
        using var client = factory.CreateApiClient();
        var product = await FirstInStockProductAsync(client);

        using var response = await GuestCheckoutAsync(client, $"guest-{Guid.CreateVersion7():N}@example.com", product.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.StartsWith("ORD-", order.GetProperty("orderNumber").GetString(), StringComparison.Ordinal);
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        Assert.Equal("NotApplicable", order.GetProperty("paymentStatus").GetString());
    }

    /// <summary>
    /// Spec §8.4: when the guest email matches an existing account, return a soft prompt and
    /// <b>do not</b> link the order. Auto-linking on an unverified email is an
    /// account-takeover vector (ADR-001).
    /// </summary>
    [Fact]
    public async Task State4_GuestEmailMatchingAnAccountPromptsButDoesNotLink()
    {
        using var anonymous = factory.CreateApiClient();
        var product = await FirstInStockProductAsync(anonymous);

        using var response = await GuestCheckoutAsync(anonymous, TestUsers.CustomerEmail, product.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = order.GetProperty("orderId").GetGuid();

        Assert.False(string.IsNullOrWhiteSpace(order.GetProperty("accountExistsPrompt").GetString()));

        // The decisive assertion: the order is a guest order, not the customer's. Reading it
        // as that customer must fail.
        using var customer = await CustomerClientAsync();
        using var attempt = await customer.GetAsync(new Uri($"/api/v1/orders/me/{orderId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);

        using var admin = await AdminClientAsync();
        using var asAdmin = await admin.GetAsync(new Uri($"/api/v1/orders/{orderId}", UriKind.Relative));
        var detail = await asAdmin.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Guest", detail.GetProperty("customerType").GetString());
    }

    [Fact]
    public async Task State4_GuestCheckoutWithNoItemsIs400()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/checkout/guest",
            new
            {
                email = "empty@example.com",
                fullName = "Empty Cart",
                shippingAddress = Address(),
                paymentMethod = "Card",
                items = Array.Empty<object>(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- checkout, stock and ownership -------------------------------------

    /// <summary>
    /// ADR-002: an unfulfillable line blocks the whole checkout with a reason per line,
    /// rather than being silently reduced to what is in stock.
    /// </summary>
    [Fact]
    public async Task Checkout_OverRequestingStockIs409WithAReasonPerLine()
    {
        using var client = factory.CreateApiClient();
        var product = await FirstInStockProductAsync(client);

        using var response = await GuestCheckoutAsync(
            client,
            $"overstock-{Guid.CreateVersion7():N}@example.com",
            product.Id,
            quantity: product.StockQuantity + 500);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("insufficient_stock", problem.GetProperty("code").GetString());

        var failures = problem.GetProperty("failures").EnumerateArray().ToArray();
        Assert.NotEmpty(failures);
        Assert.Equal(product.StockQuantity + 500, failures[0].GetProperty("requested").GetInt32());
    }

    [Fact]
    public async Task Checkout_RegisteredConvertsTheCart()
    {
        using var client = await CustomerClientAsync();
        var product = await FirstInStockProductAsync(client);

        await ClearCartAsync(client);
        await client.PostAsJsonAsync("/api/v1/carts/me/items", new { productId = product.Id, quantity = 1 });

        using var response = await client.PostAsJsonAsync(
            "/api/v1/checkout",
            new { shippingAddress = Address(), paymentMethod = "CashOnDelivery" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var cartAfter = await client.GetAsync(new Uri("/api/v1/carts/me", UriKind.Relative));
        var cart = await cartAfter.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, cart.GetProperty("totalQuantity").GetInt32());
    }

    [Fact]
    public async Task Checkout_WithAnEmptyCartIs422()
    {
        using var client = await CustomerClientAsync();
        await ClearCartAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/checkout",
            new { shippingAddress = Address(), paymentMethod = "Card" });

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
    }

    /// <summary>
    /// Spec §12 calls this out as the most common bug class in projects like this:
    /// "customer A cannot read customer B's order".
    /// </summary>
    [Fact]
    public async Task CustomerA_CannotReadCustomerBsOrder()
    {
        using var ada = await CustomerClientAsync();
        using var grace = await ClientForAsync(TestUsers.SecondCustomerLogin);

        using var adaOrders = await ada.GetAsync(new Uri("/api/v1/orders/me?pageSize=1", UriKind.Relative));
        var items = (await adaOrders.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");

        if (items.GetArrayLength() == 0)
        {
            return;
        }

        var adaOrderId = items[0].GetProperty("id").GetGuid();

        using var ownRead = await ada.GetAsync(new Uri($"/api/v1/orders/me/{adaOrderId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);

        // 404, not 403: a customer should not learn that someone else's order exists.
        using var crossRead = await grace.GetAsync(new Uri($"/api/v1/orders/me/{adaOrderId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, crossRead.StatusCode);

        // And the admin route is refused outright.
        using var adminRoute = await grace.GetAsync(new Uri($"/api/v1/orders/{adaOrderId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, adminRoute.StatusCode);
    }

    [Fact]
    public async Task AdminOrderList_ExposesCustomerTypeForFiltering()
    {
        using var admin = await AdminClientAsync();

        using var response = await admin.GetAsync(
            new Uri("/api/v1/orders?customerType=Guest&pageSize=20", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.All(
            page.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("Guest", item.GetProperty("customerType").GetString()));
    }

    // --- helpers -----------------------------------------------------------

    private sealed record SeedProduct(Guid Id, decimal Price, int StockQuantity);

    private static object Address() => new
    {
        fullName = "Test Buyer",
        line1 = "1 Test Road",
        city = "Springfield",
        state = "IL",
        postalCode = "62701",
        country = "USA",
    };

    private static async Task<HttpResponseMessage> GuestCheckoutAsync(
        HttpClient client,
        string email,
        Guid productId,
        int quantity = 1) =>
        await client.PostAsJsonAsync(
            "/api/v1/checkout/guest",
            new
            {
                email,
                fullName = "Test Guest",
                phoneNumber = "+1 555 0000",
                shippingAddress = Address(),
                paymentMethod = "CashOnDelivery",
                items = new[] { new { productId, quantity } },
            });

    private static async Task<SeedProduct> FirstInStockProductAsync(HttpClient client) =>
        (await InStockProductsAsync(client, 1))[0];

    private static async Task<IReadOnlyList<SeedProduct>> InStockProductsAsync(HttpClient client, int count)
    {
        // Highest stock first, so a test that reserves a few units cannot exhaust the row
        // and destabilise a later test.
        using var response = await client.GetAsync(
            new Uri($"/api/v1/catalog/products?inStock=true&sort=-stockQuantity&pageSize={count}", UriKind.Relative));

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("items").EnumerateArray().Select(i => new SeedProduct(
            i.GetProperty("id").GetGuid(),
            i.GetProperty("price").GetDecimal(),
            i.GetProperty("stockQuantity").GetInt32()))];
    }

    private static async Task ClearCartAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/carts/me", UriKind.Relative));
        var cart = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var item in cart.GetProperty("items").EnumerateArray())
        {
            var productId = item.GetProperty("productId").GetGuid();
            using var _ = await client.DeleteAsync(new Uri($"/api/v1/carts/me/items/{productId}", UriKind.Relative));
        }
    }

    private Task<HttpClient> CustomerClientAsync() => ClientForAsync(TestUsers.CustomerLogin);

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
