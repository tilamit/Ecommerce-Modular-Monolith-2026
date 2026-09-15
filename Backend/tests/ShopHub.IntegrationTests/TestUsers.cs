namespace ShopHub.IntegrationTests;

/// <summary>
/// The seeded development accounts (spec §13). Passwords come from the factory's
/// configuration, so they exist in exactly one place for the whole suite.
/// </summary>
internal static class TestUsers
{
    internal const string AdminEmail = "admin@shophub.local";

    /// <summary>One of the three seeded customers.</summary>
    internal const string CustomerEmail = "ada@example.com";

    internal const string SecondCustomerEmail = "grace@example.com";

    internal static object AdminLogin => new { email = AdminEmail, password = ShopHubApiFactory.AdminPassword };

    internal static object CustomerLogin => new { email = CustomerEmail, password = ShopHubApiFactory.CustomerPassword };

    internal static object SecondCustomerLogin =>
        new { email = SecondCustomerEmail, password = ShopHubApiFactory.CustomerPassword };
}
