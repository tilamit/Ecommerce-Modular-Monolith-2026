using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Identity.Features.Auth;

/// <summary>
/// <c>/api/v1/auth</c> (spec §7.3). All of these carry the <c>auth</c> rate-limit policy:
/// 5 requests per minute per IP.
/// </summary>
internal static class AuthEndpoints
{
    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).AllowAnonymous();
        group.MapPost("/logout-all", LogoutAllAsync).RequireAuthorization();
        group.MapGet("/me", MeAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        AuthService auth,
        IValidator<RegisterRequest> validator,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var (response, refreshToken, refreshExpiresUtc) =
            await auth.RegisterAsync(request, ClientIp(http), UserAgent(http), cancellationToken);

        SetRefreshCookie(http, refreshToken, refreshExpiresUtc);

        return Results.Ok(response);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AuthService auth,
        IValidator<LoginRequest> validator,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var (response, refreshToken, refreshExpiresUtc) =
            await auth.LoginAsync(request, ClientIp(http), UserAgent(http), cancellationToken);

        SetRefreshCookie(http, refreshToken, refreshExpiresUtc);

        return Results.Ok(response);
    }

    /// <summary>
    /// Reads the refresh cookie. No body, no Authorization header - by design, this is the
    /// endpoint called precisely when the access token has expired (spec §7.3).
    /// </summary>
    private static async Task<IResult> RefreshAsync(
        AuthService auth,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var presented = http.Request.Cookies[AuthenticationExtensions.RefreshTokenCookieName];

        if (string.IsNullOrWhiteSpace(presented))
        {
            // Same distinguishable code as an expired token, so the SPA takes the same
            // "redirect to login" branch instead of looping on refresh.
            ClearRefreshCookie(http);
            throw new UnauthorizedException(AuthService.RefreshExpiredCode, "Your session has expired. Please sign in again.");
        }

        try
        {
            var (response, refreshToken, refreshExpiresUtc) =
                await auth.RefreshAsync(presented, ClientIp(http), UserAgent(http), cancellationToken);

            SetRefreshCookie(http, refreshToken, refreshExpiresUtc);

            return Results.Ok(response);
        }
        catch (UnauthorizedException)
        {
            // A dead cookie would otherwise be re-presented on every retry.
            ClearRefreshCookie(http);
            throw;
        }
    }

    private static async Task<IResult> LogoutAsync(AuthService auth, HttpContext http, CancellationToken cancellationToken)
    {
        var presented = http.Request.Cookies[AuthenticationExtensions.RefreshTokenCookieName];

        await auth.LogoutAsync(presented, ClientIp(http), cancellationToken);
        ClearRefreshCookie(http);

        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(
        AuthService auth,
        ICurrentUser currentUser,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        await auth.LogoutAllAsync(currentUser.RequiredId, ClientIp(http), cancellationToken);
        ClearRefreshCookie(http);

        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        AuthService auth,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await auth.GetMeAsync(currentUser.RequiredId, cancellationToken));

    /// <summary>
    /// Spec A1: <c>HttpOnly; Secure; SameSite=Lax</c>, scoped to the auth path.
    /// <para>
    /// <c>HttpOnly</c> is the whole point - it puts the token beyond JavaScript's reach, so
    /// an XSS flaw cannot exfiltrate it and no code path can copy it into localStorage.
    /// </para>
    /// </summary>
    private static void SetRefreshCookie(HttpContext http, string token, DateTime expiresUtc) =>
        http.Response.Cookies.Append(
            AuthenticationExtensions.RefreshTokenCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                // Secure is relaxed only over plain-HTTP localhost in Development, because
                // a Secure cookie is silently dropped there and refresh would never work.
                Secure = http.Request.IsHttps || !IsDevelopment(http),
                SameSite = SameSiteMode.Lax,
                Path = AuthenticationExtensions.RefreshTokenCookiePath,
                Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero),
                IsEssential = true,
            });

    private static void ClearRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(
            AuthenticationExtensions.RefreshTokenCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps || !IsDevelopment(http),
                SameSite = SameSiteMode.Lax,
                Path = AuthenticationExtensions.RefreshTokenCookiePath,
            });

    private static bool IsDevelopment(HttpContext http) =>
        http.RequestServices.GetService(typeof(IHostEnvironment)) is IHostEnvironment env && env.IsDevelopment();

    private static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static string? UserAgent(HttpContext http) =>
        http.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null;
}
