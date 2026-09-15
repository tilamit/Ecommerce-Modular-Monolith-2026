using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShopHub.Modules.Identity.Domain;
using ShopHub.Modules.Identity.Infrastructure;
using ShopHub.Shared.Infrastructure.Security;

namespace ShopHub.Modules.Identity.Persistence;

/// <summary>
/// Seeds roles, permissions, menus and the development accounts (spec §13).
/// <para>
/// Idempotent throughout: every step is check-then-insert, never a blind insert, so running
/// it against an already-seeded database is a no-op rather than a duplicate-key crash.
/// </para>
/// </summary>
internal sealed class IdentitySeeder(
    IdentityDbContext db,
    IPasswordService passwords,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger)
{
    internal const string AdminRoleName = "Admin";
    internal const string CustomerRoleName = "Customer";
    internal const string AdminEmail = "admin@shophub.local";

    /// <summary>
    /// Configuration keys for the seed passwords. Spec §13: these come from user-secrets or
    /// an environment variable, never a literal in source.
    /// </summary>
    private const string AdminPasswordKey = "Seed:AdminPassword";

    private const string CustomerPasswordKey = "Seed:CustomerPassword";

    private static readonly (string Email, string First, string Last, string? Phone)[] CustomerSeeds =
    [
        ("ada@example.com", "Ada", "Lovelace", "+1 555 0101"),
        ("grace@example.com", "Grace", "Hopper", "+1 555 0102"),
        ("alan@example.com", "Alan", "Turing", "+1 555 0103"),
    ];

    internal async Task SeedAsync(CancellationToken cancellationToken)
    {
        var adminPassword = RequirePassword(AdminPasswordKey);
        var customerPassword = RequirePassword(CustomerPasswordKey);

        var permissions = await SeedPermissionsAsync(cancellationToken);
        var roles = await SeedRolesAsync(permissions, cancellationToken);
        var menus = await SeedMenuItemsAsync(permissions, cancellationToken);

        await SeedRoleMenusAsync(roles, menus, cancellationToken);
        await SeedUsersAsync(roles, adminPassword, customerPassword, cancellationToken);

        logger.LogInformation("Identity seed complete.");
    }

    /// <summary>
    /// Fails startup loudly rather than defaulting to something guessable (spec §13).
    /// A seeded admin account with a predictable password is a backdoor, not a convenience.
    /// </summary>
    private string RequirePassword(string key)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Seed password '{key}' is not configured. Set it before starting in Development:{Environment.NewLine}" +
                $"  dotnet user-secrets --project src/Api/ShopHub.Api set \"{key}\" \"<password>\"");
        }

        return value;
    }

    private async Task<Dictionary<string, Guid>> SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Permissions.AsTracking().ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, cancellationToken);

        foreach (var definition in Permissions.All)
        {
            if (existing.TryGetValue(definition.Code, out var permission))
            {
                // Keep display metadata in step with the code, so renaming a permission's
                // label does not require a manual database edit.
                permission.Update(definition.DisplayName, definition.Group, definition.Description);
                continue;
            }

            var created = Permission.Create(
                definition.Code,
                definition.DisplayName,
                definition.Group,
                definition.Description);

            db.Permissions.Add(created);
            existing[definition.Code] = created;
        }

        await db.SaveChangesAsync(cancellationToken);

        return existing.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Id, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, Role>> SeedRolesAsync(
        Dictionary<string, Guid> permissions,
        CancellationToken cancellationToken)
    {
        // Both collections must be loaded: ReplacePermissions and ReplaceMenuItems diff
        // against what the role already has and an unloaded collection looks empty - which
        // turns a re-seed into a blind insert and a duplicate-key crash (spec §13).
        var roles = await db.Roles
            .AsTracking()
            .Include(r => r.Permissions)
            .Include(r => r.MenuItems)
            .ToDictionaryAsync(r => r.Name, StringComparer.Ordinal, cancellationToken);

        if (!roles.TryGetValue(AdminRoleName, out var admin))
        {
            admin = Role.Create(AdminRoleName, "Full administrative access.", isSystemRole: true);
            db.Roles.Add(admin);
            roles[AdminRoleName] = admin;
        }

        if (!roles.TryGetValue(CustomerRoleName, out var customer))
        {
            customer = Role.Create(CustomerRoleName, "Storefront customer.", isSystemRole: true);
            db.Roles.Add(customer);
            roles[CustomerRoleName] = customer;
        }

        // Admin gets everything; Customer gets the *.own set (spec §13).
        admin.ReplacePermissions(permissions.Values);
        customer.ReplacePermissions(
            Permissions.CustomerDefaults
                .Where(permissions.ContainsKey)
                .Select(code => permissions[code]));

        await db.SaveChangesAsync(cancellationToken);

        return roles;
    }

    private async Task<Dictionary<string, MenuItem>> SeedMenuItemsAsync(
        Dictionary<string, Guid> permissions,
        CancellationToken cancellationToken)
    {
        var existing = await db.MenuItems
            .AsTracking()
            .ToDictionaryAsync(m => m.Title, StringComparer.Ordinal, cancellationToken);

        // Spec §8.1 names these: Dashboard, Users, Categories, Products, Offers,
        // Purchase History, Audit Trails, Access Management.
        var definitions = new (string Title, string? Icon, string? Route, string? PermissionCode, int Order)[]
        {
            ("Dashboard", "layout-dashboard", "/admin/dashboard", Shared.Infrastructure.Security.Permissions.DashboardAdmin, 10),
            ("Users", "users", "/admin/users", Shared.Infrastructure.Security.Permissions.UsersRead, 20),
            ("Categories", "folder-tree", "/admin/categories", Shared.Infrastructure.Security.Permissions.CategoriesRead, 30),
            ("Products", "package", "/admin/products", Shared.Infrastructure.Security.Permissions.ProductsRead, 40),
            ("Offers", "tag", "/admin/offers", Shared.Infrastructure.Security.Permissions.OffersRead, 50),
            ("Orders", "receipt", "/admin/orders", Shared.Infrastructure.Security.Permissions.OrdersReadAll, 60),
            ("Audit Trails", "scroll-text", "/admin/audit-trails", Shared.Infrastructure.Security.Permissions.AuditRead, 70),
            ("Access Management", "shield-check", "/admin/access", Shared.Infrastructure.Security.Permissions.AccessManage, 80),
            ("My Dashboard", "layout-dashboard", "/account/dashboard", Shared.Infrastructure.Security.Permissions.DashboardOwn, 110),
            ("Purchase History", "history", "/account/orders", Shared.Infrastructure.Security.Permissions.OrdersReadOwn, 120),
            ("My Profile", "user", "/account/profile", null, 130),
        };

        foreach (var (title, icon, route, permissionCode, order) in definitions)
        {
            Guid? requiredPermissionId = permissionCode is not null && permissions.TryGetValue(permissionCode, out var id)
                ? id
                : null;

            if (existing.TryGetValue(title, out var item))
            {
                item.Update(title, icon, route, order, isActive: true);
                continue;
            }

            var created = MenuItem.Create(title, icon, route, parentId: null, requiredPermissionId, order);
            db.MenuItems.Add(created);
            existing[title] = created;
        }

        await db.SaveChangesAsync(cancellationToken);

        return existing;
    }

    /// <summary>
    /// Grants each system role its default menus (spec §8.1).
    /// <para>
    /// Admin is granted every menu there is, including the account-area items, so an
    /// administrator is never missing a screen the application has. Customer is granted the
    /// account area only.
    /// </para>
    /// <para>
    /// Gap-filling rather than replacing. The previous version reset both roles to a fixed
    /// list on every startup, which silently undid any grant an administrator had changed
    /// through <c>/admin/access</c>. Now a menu item the role already has a row for is left
    /// exactly as it is, so a revoked menu stays revoked and an extra grant survives a
    /// restart, while a menu added in a later release still reaches its default roles.
    /// </para>
    /// </summary>
    private async Task SeedRoleMenusAsync(
        Dictionary<string, Role> roles,
        Dictionary<string, MenuItem> menus,
        CancellationToken cancellationToken)
    {
        var customerTitles = new[] { "My Dashboard", "Purchase History", "My Profile" };

        Grant(roles[AdminRoleName], menus.Keys);
        Grant(roles[CustomerRoleName], customerTitles);

        await db.SaveChangesAsync(cancellationToken);

        void Grant(Role role, IEnumerable<string> titles) =>
            role.GrantMenuItemsIfMissing(titles.Where(menus.ContainsKey).Select(t => menus[t].Id));
    }

    private async Task SeedUsersAsync(
        Dictionary<string, Role> roles,
        string adminPassword,
        string customerPassword,
        CancellationToken cancellationToken)
    {
        await EnsureUserAsync(AdminEmail, "Site", "Administrator", null, adminPassword, roles[AdminRoleName].Id, cancellationToken);

        foreach (var (email, first, last, phone) in CustomerSeeds)
        {
            await EnsureUserAsync(email, first, last, phone, customerPassword, roles[CustomerRoleName].Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUserAsync(
        string email,
        string firstName,
        string lastName,
        string? phone,
        string password,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var normalized = User.Normalize(email);

        // A deleted development account counts as existing, so deleting it is not undone on
        // the next start.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            return;
        }

        var user = User.Create(email, passwords.Hash(password), firstName, lastName, phone);
        user.ConfirmEmail();
        user.AssignRole(roleId);

        db.Users.Add(user);
    }
}
