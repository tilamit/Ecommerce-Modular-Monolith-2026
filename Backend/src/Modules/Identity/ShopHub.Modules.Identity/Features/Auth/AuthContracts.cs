namespace ShopHub.Modules.Identity.Features.Auth;

/// <summary>
/// The login/register/refresh response (spec A1).
/// <para>
/// Note what is absent: the refresh token itself. It travels only as an HttpOnly cookie,
/// so JavaScript can never read it - the SPA learns nothing but its expiry.
/// </para>
/// </summary>
internal sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresUtc,
    DateTime RefreshTokenExpiresUtc,
    UserProfileResponse User);

/// <summary>
/// The outcome of a refresh attempt (spec §7.3).
/// <para>
/// A refresh that does not succeed is an expected result: a visitor who has never signed in,
/// a stale cookie, a session abandoned overnight. Modelling it as a value keeps the anonymous
/// first-load path off the throw path, with no stack capture and no debugger stop.
/// </para>
/// <para>
/// The caller must not explain why it failed. Unknown, expired, revoked and reuse-detected
/// all collapse to the same 401 with the same <c>refresh_expired</c> code, so a presented
/// token cannot be probed for its state.
/// </para>
/// </summary>
internal readonly record struct RefreshOutcome(
    AuthResponse? Response,
    string? RefreshToken,
    DateTime RefreshExpiresUtc)
{
    /// <summary>The session could not be renewed. The caller answers 401 and clears the cookie.</summary>
    internal static RefreshOutcome Failed => default;

    internal bool Succeeded => Response is not null && RefreshToken is not null;
}

internal sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    bool IsActive,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

internal sealed record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber);

internal sealed record LoginRequest(string Email, string Password);

/// <summary>
/// <c>/auth/me</c> (spec §7.3): profile, roles and the resolved dynamic menu tree the SPA
/// renders its sidebar from.
/// </summary>
internal sealed record MeResponse(UserProfileResponse Profile, IReadOnlyList<MenuNodeResponse> Menu);

internal sealed record MenuNodeResponse(
    Guid Id,
    string Title,
    string? Icon,
    string? Route,
    int DisplayOrder,
    IReadOnlyList<MenuNodeResponse> Children);
