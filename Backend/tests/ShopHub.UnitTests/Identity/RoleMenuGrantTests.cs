using ShopHub.Modules.Identity.Domain;

namespace ShopHub.UnitTests.Identity;

/// <summary>
/// The two ways a role's menu grants change and the difference between them.
/// <para>
/// <c>ReplaceMenuItems</c> is the admin screen: what is sent is what the role ends up with.
/// <c>GrantMenuItemsIfMissing</c> is the seeder: it fills gaps and never overrules a
/// decision already recorded, which is what lets an administrator's changes survive a
/// restart.
/// </para>
/// </summary>
public sealed class RoleMenuGrantTests
{
    private static readonly Guid Dashboard = Guid.CreateVersion7();
    private static readonly Guid Orders = Guid.CreateVersion7();
    private static readonly Guid AuditTrails = Guid.CreateVersion7();

    private static Role NewRole() => Role.Create("Customer", "Storefront customer.", isSystemRole: true);

    [Fact]
    public void GrantMenuItemsIfMissing_AddsMenusTheRoleDoesNotHave()
    {
        var role = NewRole();

        role.GrantMenuItemsIfMissing([Dashboard, Orders]);

        Assert.Equal(2, role.MenuItems.Count);
        Assert.All(role.MenuItems, m => Assert.True(m.IsVisible));
    }

    /// <summary>
    /// The regression this exists for. The seeder used to call <c>ReplaceMenuItems</c> with a
    /// fixed list on every startup, so a menu an administrator granted through access
    /// management was silently removed the next time the application started.
    /// </summary>
    [Fact]
    public void GrantMenuItemsIfMissing_KeepsAGrantTheSeedListDoesNotMention()
    {
        var role = NewRole();

        role.ReplaceMenuItems([(Dashboard, true), (AuditTrails, true)]);

        // The seed defaults do not include AuditTrails.
        role.GrantMenuItemsIfMissing([Dashboard, Orders]);

        Assert.Contains(role.MenuItems, m => m.MenuItemId == AuditTrails && m.IsVisible);
        Assert.Equal(3, role.MenuItems.Count);
    }

    /// <summary>
    /// A revoked menu is a decision too. Re-granting it on every startup would make the
    /// admin screen's revoke button appear to work and then quietly undo itself.
    /// </summary>
    [Fact]
    public void GrantMenuItemsIfMissing_LeavesARevokedMenuRevoked()
    {
        var role = NewRole();

        role.ReplaceMenuItems([(Dashboard, false)]);
        role.GrantMenuItemsIfMissing([Dashboard]);

        var grant = Assert.Single(role.MenuItems);
        Assert.False(grant.IsVisible);
    }

    [Fact]
    public void GrantMenuItemsIfMissing_IgnoresDuplicates()
    {
        var role = NewRole();

        role.GrantMenuItemsIfMissing([Dashboard, Dashboard, Orders]);

        Assert.Equal(2, role.MenuItems.Count);
    }

    /// <summary>
    /// The admin screen still replaces. Anything absent from the request is dropped, which
    /// is what makes unchecking a box remove the grant rather than hide it.
    /// </summary>
    [Fact]
    public void ReplaceMenuItems_DropsWhatIsNotSent()
    {
        var role = NewRole();

        role.ReplaceMenuItems([(Dashboard, true), (Orders, true)]);
        role.ReplaceMenuItems([(Orders, true)]);

        var grant = Assert.Single(role.MenuItems);
        Assert.Equal(Orders, grant.MenuItemId);
    }
}
