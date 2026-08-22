using ShopHub.Modules.Identity.Domain;

namespace ShopHub.UnitTests.Identity;

/// <summary>
/// Spec §12 names "refresh-token rotation + reuse detection" as required unit coverage.
/// These cover the domain rules; the end-to-end HTTP behaviour is asserted in
/// <c>AuthLifecycleTests</c>.
/// </summary>
public sealed class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshToken Issue(
        DateTime? nowUtc = null,
        TimeSpan? lifetime = null,
        DateTime? absoluteExpiryUtc = null) =>
        RefreshToken.Issue(
            userId: Guid.CreateVersion7(),
            tokenHash: "hash",
            familyId: Guid.CreateVersion7(),
            nowUtc: nowUtc ?? Now,
            lifetime: lifetime ?? TimeSpan.FromDays(7),
            absoluteExpiryUtc: absoluteExpiryUtc ?? (nowUtc ?? Now).AddDays(30),
            createdByIp: "127.0.0.1",
            userAgent: "tests");

    [Fact]
    public void Issue_CreatesAnActiveToken()
    {
        var token = Issue();

        Assert.True(token.IsActive(Now));
        Assert.False(token.IsRevoked);
        Assert.Equal(Now.AddDays(7), token.ExpiresUtc);
    }

    /// <summary>
    /// Spec §7.1: rotation slides the 7-day window forward, but never past the 30-day
    /// absolute cap. Without this clamp an attacker holding a live token could renew it
    /// indefinitely and the "absolute cap" would mean nothing.
    /// </summary>
    [Fact]
    public void Issue_ClampsSlidingExpiryToTheAbsoluteCap()
    {
        var almostAtTheCap = Now.AddDays(2);

        var token = Issue(
            nowUtc: almostAtTheCap,
            lifetime: TimeSpan.FromDays(7),
            absoluteExpiryUtc: Now.AddDays(3));

        Assert.Equal(Now.AddDays(3), token.ExpiresUtc);
    }

    [Fact]
    public void IsExpired_WhenTheSlidingWindowHasPassed()
    {
        var token = Issue();

        Assert.False(token.IsExpired(Now.AddDays(6)));
        Assert.True(token.IsExpired(Now.AddDays(8)));
    }

    [Fact]
    public void IsExpired_WhenTheAbsoluteCapHasPassed()
    {
        var token = Issue(lifetime: TimeSpan.FromDays(365), absoluteExpiryUtc: Now.AddDays(30));

        Assert.True(token.IsExpired(Now.AddDays(31)));
    }

    [Fact]
    public void RotateInto_RevokesAndChainsToTheReplacement()
    {
        var token = Issue();
        var replacementId = Guid.CreateVersion7();

        token.RotateInto(replacementId, Now.AddMinutes(20), "10.0.0.1");

        Assert.True(token.IsRevoked);
        Assert.False(token.IsActive(Now.AddMinutes(20)));
        Assert.Equal(replacementId, token.ReplacedByTokenId);
        Assert.Equal("rotated", token.RevokedReason);
    }

    /// <summary>
    /// Revocation is the fact that drives reuse detection, so it must not be silently
    /// overwritten by a later revoke with a different reason - the original cause is what
    /// an operator needs when investigating.
    /// </summary>
    [Fact]
    public void Revoke_IsIdempotentAndKeepsTheFirstReason()
    {
        var token = Issue();

        token.Revoke(Now.AddMinutes(5), "10.0.0.1", "logout");
        token.Revoke(Now.AddMinutes(9), "10.0.0.2", "reuse_detected");

        Assert.Equal("logout", token.RevokedReason);
        Assert.Equal(Now.AddMinutes(5), token.RevokedUtc);
        Assert.Equal("10.0.0.1", token.RevokedByIp);
    }

    [Fact]
    public void ARevokedToken_IsNeverActive()
    {
        var token = Issue();

        token.Revoke(Now, null, "logout");

        Assert.False(token.IsActive(Now));
        Assert.False(token.IsExpired(Now));
    }

    /// <summary>
    /// Rotation keeps the family id, which is what lets reuse detection revoke every
    /// descendant of one sign-in in a single statement.
    /// </summary>
    [Fact]
    public void Rotation_PreservesTheFamilyId()
    {
        var original = Issue();

        var rotated = RefreshToken.Issue(
            userId: Guid.CreateVersion7(),
            tokenHash: "next",
            familyId: original.FamilyId,
            nowUtc: Now.AddMinutes(20),
            lifetime: TimeSpan.FromDays(7),
            absoluteExpiryUtc: original.AbsoluteExpiryUtc,
            createdByIp: null,
            userAgent: null);

        Assert.Equal(original.FamilyId, rotated.FamilyId);
        Assert.Equal(original.AbsoluteExpiryUtc, rotated.AbsoluteExpiryUtc);
    }
}
