using ShopHub.Shared.Kernel.Entities;

namespace ShopHub.Modules.Identity.Domain;

/// <summary>
/// A server-side refresh token record (spec §7.2). Only the SHA-256 <see cref="TokenHash"/>
/// is stored - the plaintext exists exactly once, in the response cookie.
/// <para>
/// Tokens form a <em>family</em>: every rotation issues a new row chained by
/// <see cref="ReplacedByTokenId"/> and sharing a <see cref="FamilyId"/>. If an already-
/// revoked token is presented, the whole family is revoked - that is the signature of a
/// stolen token and the legitimate holder must re-authenticate.
/// </para>
/// </summary>
internal sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTime createdUtc,
        DateTime expiresUtc,
        DateTime absoluteExpiryUtc,
        string? createdByIp,
        string? userAgent)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedUtc = createdUtc;
        ExpiresUtc = expiresUtc;
        AbsoluteExpiryUtc = absoluteExpiryUtc;
        CreatedByIp = createdByIp;
        UserAgent = userAgent;
    }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the token. Uniquely indexed; lookups never use plaintext.</summary>
    [NoAudit]
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Shared by every token descended from one login. Revoked as a unit on reuse.</summary>
    public Guid FamilyId { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime ExpiresUtc { get; private set; }

    /// <summary>
    /// The 30-day cap from spec §7.1. Rotation slides <see cref="ExpiresUtc"/> forward but
    /// never past this, so an attacker with a live token cannot renew it indefinitely.
    /// </summary>
    public DateTime AbsoluteExpiryUtc { get; private set; }

    public string? CreatedByIp { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTime? RevokedUtc { get; private set; }

    public string? RevokedByIp { get; private set; }

    public string? RevokedReason { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsRevoked => RevokedUtc is not null;

    public bool IsExpired(DateTime nowUtc) => ExpiresUtc <= nowUtc || AbsoluteExpiryUtc <= nowUtc;

    public bool IsActive(DateTime nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    public static RefreshToken Issue(
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTime nowUtc,
        TimeSpan lifetime,
        DateTime absoluteExpiryUtc,
        string? createdByIp,
        string? userAgent)
    {
        // Sliding expiry must never outrun the absolute cap.
        var expiresUtc = nowUtc.Add(lifetime);

        if (expiresUtc > absoluteExpiryUtc)
        {
            expiresUtc = absoluteExpiryUtc;
        }

        return new RefreshToken(
            SequentialGuid.New(),
            userId,
            tokenHash,
            familyId,
            nowUtc,
            expiresUtc,
            absoluteExpiryUtc,
            createdByIp,
            userAgent);
    }

    public void Revoke(DateTime nowUtc, string? revokedByIp, string reason)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedUtc = nowUtc;
        RevokedByIp = revokedByIp;
        RevokedReason = reason;
    }

    /// <summary>Marks this token as rotated into <paramref name="replacementId"/>.</summary>
    public void RotateInto(Guid replacementId, DateTime nowUtc, string? revokedByIp)
    {
        Revoke(nowUtc, revokedByIp, RevocationReasons.Rotated);
        ReplacedByTokenId = replacementId;
    }
}

/// <summary>
/// Why a token was revoked. Named rather than free text so the audit trail and the
/// reuse-detection tests can assert on them.
/// </summary>
internal static class RevocationReasons
{
    internal const string Rotated = "rotated";
    internal const string Logout = "logout";
    internal const string LogoutAll = "logout_all";
    internal const string ReuseDetected = "reuse_detected";
    internal const string DeviceCapExceeded = "device_cap_exceeded";
    internal const string UserDeactivated = "user_deactivated";
}
