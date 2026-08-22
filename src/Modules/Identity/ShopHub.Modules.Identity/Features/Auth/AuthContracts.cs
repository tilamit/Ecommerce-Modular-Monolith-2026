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
/// <c>/auth/me</c> (spec §7.3): profile, roles, and the resolved dynamic menu tree the SPA
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
