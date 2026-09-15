namespace ShopHub.Shared.Infrastructure.Security;

/// <summary>
/// Token settings (spec §7.1). Bound from the <c>Jwt</c> configuration section; the signing
/// key must come from user-secrets or an environment variable, never from
/// <c>appsettings.json</c>.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ShopHub";

    public string Audience { get; set; } = "ShopHub.Spa";

    /// <summary>
    /// HS256 signing key. Minimum 32 bytes (spec §7.1) - a shorter key silently weakens
    /// every token issued, so <see cref="Validate"/> refuses to start without one.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Spec §7.1: 15 minutes. Short enough that a leaked token expires quickly.
    /// <para>
    /// A <see cref="TimeSpan"/> rather than a whole number of minutes, so sub-minute
    /// lifetimes are expressible. Spec §14 Phase 7 requires exercising silent refresh with
    /// the TTL set to 30 seconds - a configuration knob that cannot express the value its
    /// own acceptance test needs is the wrong knob.
    /// </para>
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Spec §7.1: 7 days, sliding via rotation.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Spec §7.1: absolute cap of 30 days, regardless of how often it is rotated.</summary>
    public TimeSpan RefreshTokenAbsoluteCap { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Spec §7.2: cap active tokens per user; the oldest beyond this is revoked.</summary>
    public int MaxActiveTokensPerUser { get; set; } = 5;

    /// <summary>Spec §7.3: lockout after 5 consecutive failures.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>Spec §7.3: lockout lasts 15 minutes.</summary>
    public int LockoutMinutes { get; set; } = 15;

    public const int MinimumSigningKeyBytes = 32;

    /// <summary>
    /// Fails startup loudly rather than defaulting to something guessable. A weak or
    /// missing signing key is not a misconfiguration to warn about - it is a forgeable
    /// token for every user in the system.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it with: " +
                "dotnet user-secrets --project src/Api/ShopHub.Api set \"Jwt:SigningKey\" \"<at least 32 characters>\"");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(SigningKey) < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinimumSigningKeyBytes} bytes for HS256.");
        }
    }
}
