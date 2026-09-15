namespace ShopHub.Shared.Infrastructure.Security;

/// <summary>
/// The permission vocabulary (spec §7.5). Endpoints require a <em>permission</em>, never a
/// role name, so adding a third role is a data change rather than a deploy.
/// <para>
/// These constants exist so an endpoint cannot require a permission that was never seeded:
/// the seeder and the endpoints read the same list.
/// </para>
/// </summary>
public static class Permissions
{
    // --- Catalog ---------------------------------------------------------
    public const string ProductsRead = "catalog.products.read";
    public const string ProductsWrite = "catalog.products.write";
    public const string CategoriesRead = "catalog.categories.read";
    public const string CategoriesWrite = "catalog.categories.write";
    public const string OffersRead = "catalog.offers.read";
    public const string OffersWrite = "catalog.offers.write";

    // --- Identity --------------------------------------------------------
    public const string UsersRead = "identity.users.read";
    public const string UsersManage = "identity.users.manage";
    public const string RolesRead = "identity.roles.read";
    public const string RolesManage = "identity.roles.manage";
    public const string AccessManage = "identity.access.manage";

    // --- Ordering --------------------------------------------------------
    public const string OrdersReadAll = "orders.read.all";
    public const string OrdersReadOwn = "orders.read.own";
    public const string OrdersManage = "orders.manage";
    public const string CartOwn = "cart.own";
    public const string CheckoutOwn = "checkout.own";

    // --- Auditing --------------------------------------------------------
    public const string AuditRead = "audit.read";

    // --- Dashboards ------------------------------------------------------
    public const string DashboardAdmin = "dashboard.admin";
    public const string DashboardOwn = "dashboard.own";

    /// <summary>
    /// Every permission, with the display name and grouping the admin UI needs
    /// (spec §8.1: <c>Permissions.Group</c> exists "for the admin UI").
    /// </summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ProductsRead, "View products", "Catalog", "Read the product catalogue."),
        new(ProductsWrite, "Manage products", "Catalog", "Create, edit and deactivate products."),
        new(CategoriesRead, "View categories", "Catalog", "Read the category tree."),
        new(CategoriesWrite, "Manage categories", "Catalog", "Create, edit and deactivate categories."),
        new(OffersRead, "View offers", "Catalog", "Read discount offers."),
        new(OffersWrite, "Manage offers", "Catalog", "Create, edit and expire discount offers."),

        new(UsersRead, "View users", "Identity", "Read the user directory."),
        new(UsersManage, "Manage users", "Identity", "Create, edit, deactivate and assign roles to users."),
        new(RolesRead, "View roles", "Identity", "Read roles and their permissions."),
        new(RolesManage, "Manage roles", "Identity", "Create, edit and delete non-system roles."),
        new(AccessManage, "Manage access", "Identity", "Grant permissions and menu visibility to roles."),

        new(OrdersReadAll, "View all orders", "Ordering", "Read every customer's orders."),
        new(OrdersReadOwn, "View own orders", "Ordering", "Read your own purchase history."),
        new(OrdersManage, "Manage orders", "Ordering", "Change order status."),
        new(CartOwn, "Use a cart", "Ordering", "Add to and modify your own cart."),
        new(CheckoutOwn, "Check out", "Ordering", "Place an order."),

        new(AuditRead, "View audit trail", "Auditing", "Read the append-only audit trail."),

        new(DashboardAdmin, "Admin dashboard", "Dashboards", "View the administrative dashboard."),
        new(DashboardOwn, "Customer dashboard", "Dashboards", "View your own dashboard."),
    ];

    /// <summary>
    /// What a Customer gets: their own cart, their own orders, their own dashboard, plus
    /// read access to the storefront. Deliberately the <c>*.own</c> set (spec §13).
    /// </summary>
    public static IReadOnlyList<string> CustomerDefaults { get; } =
    [
        ProductsRead,
        CategoriesRead,
        OffersRead,
        OrdersReadOwn,
        CartOwn,
        CheckoutOwn,
        DashboardOwn,
    ];
}

/// <summary>A permission as seeded into <c>identity.Permissions</c>.</summary>
public sealed record PermissionDefinition(string Code, string DisplayName, string Group, string Description);
