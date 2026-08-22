using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ShopHub.Shared.Infrastructure.Security;

/// <summary>
/// JWT bearer authentication and the permission-based authorization stack (spec §7).
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>Cookie carrying the refresh token (spec A1). Never readable by JavaScript.</summary>
    public const string RefreshTokenCookieName = "shophub_rt";

    /// <summary>
    /// Path the refresh cookie is scoped to, so it is not attached to every API call -
    /// only to the endpoints that actually need it.
    /// </summary>
    public const string RefreshTokenCookiePath = "/api/v1/auth";

    public static IServiceCollection AddShopHubAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "Jwt configuration is invalid.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // The signing key is read from IOptions here rather than from the IConfiguration
        // captured above. Reading it eagerly at registration time would freeze whichever
        // configuration existed at that moment, so a source added later would be ignored -
        // and the token issuer (which does resolve IOptions) would sign with a different
        // key than the validator checks against. Every token would then fail validation
        // with no error detail, because IncludeErrorDetails is off.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptionsAccessor) =>
            {
                var jwt = jwtOptionsAccessor.Value;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

                    // Default is 5 minutes, which would keep a 15-minute token valid for
                    // 20. The SPA refreshes proactively, so no slack is needed.
                    ClockSkew = TimeSpan.Zero,

                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                };

                // Never echo token contents or validation internals to the caller (spec §6.2).
                bearer.IncludeErrorDetails = false;

                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        // Distinguish "token expired" from "no token", so the SPA knows to
                        // refresh rather than bounce straight to the login page.
                        if (context.AuthenticateFailure is SecurityTokenExpiredException)
                        {
                            context.Response.Headers["X-Token-Expired"] = "true";
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();

        return services;
    }
}
