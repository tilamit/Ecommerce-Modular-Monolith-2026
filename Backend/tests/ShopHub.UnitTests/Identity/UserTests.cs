using ShopHub.Modules.Identity.Domain;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.UnitTests.Identity;

/// <summary>
/// Lockout and role rules live in the domain, so they hold regardless of which caller
/// reaches them (spec §7.3).
/// </summary>
public sealed class UserTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private const int MaxAttempts = 5;
    private const int LockoutMinutes = 15;

    private static User Create() => User.Create("Ada@Example.com", "hash", "Ada", "Lovelace");

    [Fact]
    public void Create_NormalizesTheEmailForLookup()
    {
        var user = Create();

        Assert.Equal("Ada@Example.com", user.Email);
        Assert.Equal("ADA@EXAMPLE.COM", user.NormalizedEmail);
    }

    [Fact]
    public void Create_StartsActiveAndUnlocked()
    {
        var user = Create();

        Assert.True(user.IsActive);
        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Fact]
    public void FailedLogins_BelowTheThreshold_DoNotLock()
    {
        var user = Create();

        for (var i = 0; i < MaxAttempts - 1; i++)
        {
            user.RegisterFailedLogin(Now, MaxAttempts, LockoutMinutes);
        }

        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(MaxAttempts - 1, user.AccessFailedCount);
    }

    [Fact]
    public void FailedLogins_AtTheThreshold_LockForTheConfiguredWindow()
    {
        var user = Create();

        for (var i = 0; i < MaxAttempts; i++)
        {
            user.RegisterFailedLogin(Now, MaxAttempts, LockoutMinutes);
        }

        Assert.True(user.IsLockedOut(Now));
        Assert.True(user.IsLockedOut(Now.AddMinutes(LockoutMinutes - 1)));

        // The lock lifts on its own; it is a delay, not a permanent ban.
        Assert.False(user.IsLockedOut(Now.AddMinutes(LockoutMinutes + 1)));
    }

    /// <summary>
    /// Spec §7.3 says lockout follows five <em>consecutive</em> failures, so a success in
    /// between must reset the streak - otherwise a user who mistypes occasionally over
    /// weeks would eventually lock themselves out for no reason.
    /// </summary>
    [Fact]
    public void ASuccessfulLogin_ResetsTheFailureStreak()
    {
        var user = Create();

        user.RegisterFailedLogin(Now, MaxAttempts, LockoutMinutes);
        user.RegisterFailedLogin(Now, MaxAttempts, LockoutMinutes);
        user.RegisterSuccessfulLogin(Now);

        Assert.Equal(0, user.AccessFailedCount);
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(Now, user.LastLoginUtc);
    }

    [Fact]
    public void ChangingEmail_ResetsConfirmation()
    {
        var user = Create();
        user.ConfirmEmail();

        user.ChangeEmail("grace@example.com");

        Assert.False(user.IsEmailConfirmed);
        Assert.Equal("GRACE@EXAMPLE.COM", user.NormalizedEmail);
    }

    [Fact]
    public void AssignRole_IsIdempotent()
    {
        var user = Create();
        var roleId = Guid.CreateVersion7();

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        Assert.Single(user.Roles);
    }

    [Fact]
    public void ReplaceRoles_AddsAndRemovesToMatchTheTarget()
    {
        var user = Create();
        var keep = Guid.CreateVersion7();
        var drop = Guid.CreateVersion7();
        var add = Guid.CreateVersion7();

        user.AssignRole(keep);
        user.AssignRole(drop);

        user.ReplaceRoles([keep, add]);

        Assert.Equal(2, user.Roles.Count);
        Assert.Contains(user.Roles, r => r.RoleId == keep);
        Assert.Contains(user.Roles, r => r.RoleId == add);
        Assert.DoesNotContain(user.Roles, r => r.RoleId == drop);
    }

    /// <summary>
    /// A user with no role has no permissions and cannot be authorized for anything, which
    /// is a broken account rather than a valid state - so the domain refuses it outright.
    /// </summary>
    [Fact]
    public void ReplaceRoles_RefusesAnEmptySet()
    {
        var user = Create();
        user.AssignRole(Guid.CreateVersion7());

        var exception = Assert.Throws<DomainRuleException>(() => user.ReplaceRoles([]));

        Assert.Equal("user_requires_role", exception.Code);
    }
}
