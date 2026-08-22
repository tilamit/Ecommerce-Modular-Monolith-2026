using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShopHub.Modules.Identity.Domain;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Identity.Infrastructure;

/// <summary>Issues access tokens and the opaque refresh tokens that renew them (spec §7.1).</summary>
internal interface ITokenService
{
    (string Token, DateTime ExpiresUtc) CreateAccessToken(User user, IReadOnlyCollection<string> roleNames);

    /// <summary>
    /// Returns the plaintext refresh token and its hash. The plaintext is handed to the
    /// cookie and then forgotten; only the hash is persisted.
    /// </summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}

internal sealed class TokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    /// <summary>Spec §7.1: 32 cryptographically-random bytes, base64url.</summary>
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresUtc) CreateAccessToken(User user, IReadOnlyCollection<string> roleNames)
    {
        var now = clock.UtcNow;
        var expiresUtc = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("N")),
        };

        // Roles may repeat. Permissions deliberately do NOT go in the token (spec §7.1):
        // they change, and a 15-minute token would keep serving revoked ones.
        claims.AddRange(roleNames.Select(role => new Claim(ClaimTypes.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresUtc);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        var token = Base64UrlEncoder.Encode(bytes);

        return (token, HashRefreshToken(token));
    }

    /// <summary>
    /// SHA-256, not a password hash. The token is 256 bits of entropy from a CSPRNG, so it
    /// is not brute-forceable and does not need a slow KDF - and a slow hash here would put
    /// a deliberate delay on every refresh.
    /// </summary>
    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
