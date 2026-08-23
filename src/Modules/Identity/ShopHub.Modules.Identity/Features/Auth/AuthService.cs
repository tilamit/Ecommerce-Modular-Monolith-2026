using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Identity.Domain;
using ShopHub.Modules.Identity.Infrastructure;
using ShopHub.Modules.Identity.Persistence;
using ShopHub.Shared.Contracts.Events;
using ShopHub.Shared.Infrastructure.Events;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Entities;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Infrastructure.Security;

namespace ShopHub.Modules.Identity.Features.Auth;

/// <summary>
/// Registration, sign-in, and the refresh-token lifecycle (spec §7.2-§7.4).
/// <para>
/// Everything security-sensitive about authentication lives here rather than in the
/// endpoints, so the rules cannot diverge between callers.
/// </para>
/// </summary>
internal sealed class AuthService(
    IdentityDbContext db,
    ITokenService tokens,
    IPasswordService passwords,
    IPermissionService permissions,
    IMenuService menus,
    IClock clock,
    IEventBus eventBus,
    IAuditWriter audit,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    /// <summary>
    /// Identical message for an unknown email and a wrong password (spec §7.3). Anything
    /// more specific is an account-enumeration oracle.
    /// </summary>
    private const string InvalidCredentialsMessage = "Email or password is incorrect.";

    internal const string RefreshExpiredCode = "refresh_expired";

    internal async Task<(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresUtc)> RegisterAsync(
        RegisterRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var normalized = User.Normalize(request.Email);

        var emailTaken = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        if (emailTaken)
        {
            throw new ConflictException("email_taken", "An account with that email already exists.");
        }

        var customerRole = await db.Roles
            .FirstOrDefaultAsync(r => r.NormalizedName == "CUSTOMER", cancellationToken)
            ?? throw new DomainRuleException("customer_role_missing", "The Customer role has not been seeded.");

        var user = User.Create(
            request.Email,
            passwords.Hash(request.Password),
            request.FirstName,
            request.LastName,
            request.PhoneNumber);

        user.AssignRole(customerRole.Id);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Registered user {UserId}.", user.Id);
        }

        // Published after the transaction commits (spec §4.3). Ordering consumes this to
        // link any prior guest-checkout history to the new account - the only moment
        // linking is safe, because the address is now demonstrably controlled (ADR-001).
        await eventBus.PublishAsync(
            new UserRegisteredIntegrationEvent(
                SequentialGuid.New(),
                clock.UtcNow,
                user.Id,
                user.Email,
                user.FullName),
            cancellationToken);

        return await IssueSessionAsync(user, familyId: null, ipAddress, userAgent, cancellationToken);
    }

    internal async Task<(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresUtc)> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var normalized = User.Normalize(request.Email);
        var now = clock.UtcNow;

        // AsTracking: this user is mutated below (failure counters, last-login stamp).
        // The context defaults to NoTracking per spec §6.7, so writes must opt in.
        var user = await db.Users
            .AsTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        if (user is null)
        {
            // Burn equivalent CPU so an unknown email is not faster than a wrong password.
            passwords.VerifyDummy(request.Password);

            // Recorded even though there is no account: repeated failures against
            // non-existent emails are what account enumeration looks like from the outside.
            WriteAuthAudit(AuditAction.LoginFailed, userId: null, request.Email);

            throw new ForbiddenException("invalid_credentials", InvalidCredentialsMessage);
        }

        // Checked before verifying the password, so a locked account cannot be probed.
        if (user.IsLockedOut(now))
        {
            throw new ForbiddenException(
                "account_locked",
                "This account is temporarily locked after too many failed sign-in attempts. Try again shortly.");
        }

        var verification = passwords.Verify(user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, _jwt.MaxFailedAccessAttempts, _jwt.LockoutMinutes);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning("Failed sign-in for user {UserId}.", user.Id);
            WriteAuthAudit(AuditAction.LoginFailed, user.Id, user.Email);

            throw new ForbiddenException("invalid_credentials", InvalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException("account_inactive", "This account has been deactivated.");
        }

        // The hasher can tell us the stored hash used outdated parameters.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwords.Hash(request.Password));
        }

        user.RegisterSuccessfulLogin(now);
        await db.SaveChangesAsync(cancellationToken);

        WriteAuthAudit(AuditAction.Login, user.Id, user.Email);

        return await IssueSessionAsync(user, familyId: null, ipAddress, userAgent, cancellationToken);
    }

    /// <summary>
    /// Rotates a refresh token (spec §7.2).
    /// <para>
    /// The presented token is revoked and chained to its replacement. If a token that was
    /// <em>already</em> revoked is presented, the entire family is revoked immediately:
    /// either the token was stolen and replayed, or the legitimate client replayed one an
    /// attacker already used. Both cases warrant forcing a fresh sign-in.
    /// </para>
    /// </summary>
    internal async Task<(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresUtc)> RefreshAsync(
        string presentedToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = tokens.HashRefreshToken(presentedToken);

        // AsTracking: the presented token is revoked and chained to its replacement.
        var existing = await db.RefreshTokens
            .AsTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            throw new UnauthorizedException(RefreshExpiredCode, "Your session has expired. Please sign in again.");
        }

        if (existing.IsRevoked)
        {
            await RevokeFamilyAsync(existing.FamilyId, now, ipAddress, RevocationReasons.ReuseDetected, cancellationToken);

            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; revoked token family {FamilyId}.",
                existing.UserId,
                existing.FamilyId);

            // Spec §7.2 asks for an audit entry here specifically: this is the signature of
            // a stolen token, and it is the one event an operator most needs to find later.
            audit.Write(new AuditEntryDto
            {
                Action = AuditAction.PermissionChange,
                Module = "identity",
                EntityName = nameof(RefreshToken),
                EntityId = existing.Id.ToString(),
                UserId = existing.UserId,
                NewValues = $"{{\"reuseDetected\":true,\"familyId\":\"{existing.FamilyId}\"}}",
            });

            throw new UnauthorizedException(RefreshExpiredCode, "Your session has expired. Please sign in again.");
        }

        if (existing.IsExpired(now))
        {
            throw new UnauthorizedException(RefreshExpiredCode, "Your session has expired. Please sign in again.");
        }

        var user = await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == existing.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            await RevokeFamilyAsync(existing.FamilyId, now, ipAddress, RevocationReasons.UserDeactivated, cancellationToken);
            throw new UnauthorizedException(RefreshExpiredCode, "Your session has expired. Please sign in again.");
        }

        var issued = await IssueSessionAsync(
            user,
            existing.FamilyId,
            ipAddress,
            userAgent,
            cancellationToken,
            absoluteExpiryUtc: existing.AbsoluteExpiryUtc,
            rotatedFrom: existing);

        return issued;
    }

    internal async Task LogoutAsync(string? presentedToken, string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return;
        }

        var hash = tokens.HashRefreshToken(presentedToken);

        // AsTracking: the token is revoked below.
        var existing = await db.RefreshTokens
            .AsTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.Revoke(clock.UtcNow, ipAddress, RevocationReasons.Logout);
        await db.SaveChangesAsync(cancellationToken);

        WriteAuthAudit(AuditAction.Logout, existing.UserId, userName: null);
    }

    /// <summary>Revokes every active token for a user (spec §7.3 <c>/logout-all</c>).</summary>
    internal async Task LogoutAllAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // ExecuteUpdateAsync bypasses the change tracker - and therefore the audit
        // interceptor (spec §6.7). The audit entry for this is written by the endpoint.
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedUtc, now)
                    .SetProperty(t => t.RevokedByIp, ipAddress)
                    .SetProperty(t => t.RevokedReason, RevocationReasons.LogoutAll),
                cancellationToken);

        // ExecuteUpdateAsync bypasses the change tracker and therefore the audit
        // interceptor (spec §6.7), so the entry is written explicitly here.
        WriteAuthAudit(AuditAction.Logout, userId, userName: null);
    }

    internal async Task<MeResponse> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw NotFoundException.For("User", userId);

        var profile = await BuildProfileAsync(user, cancellationToken);
        var menu = await menus.GetMenuForRolesAsync(user.Roles.Select(r => r.RoleId).ToArray(), cancellationToken);

        return new MeResponse(profile, menu);
    }

    /// <summary>
    /// Issues an access token plus a new refresh token, enforcing the device cap.
    /// When <paramref name="rotatedFrom"/> is supplied the old token is chained to the new
    /// one; both writes commit in a single SaveChanges so a crash cannot leave a token
    /// revoked with no replacement.
    /// </summary>
    private async Task<(AuthResponse, string, DateTime)> IssueSessionAsync(
        User user,
        Guid? familyId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken,
        DateTime? absoluteExpiryUtc = null,
        RefreshToken? rotatedFrom = null)
    {
        var now = clock.UtcNow;
        var roleNames = await RoleNamesAsync(user, cancellationToken);

        var (accessToken, accessExpiresUtc) = tokens.CreateAccessToken(user, roleNames);
        var (refreshPlaintext, refreshHash) = tokens.CreateRefreshToken();

        var refreshToken = RefreshToken.Issue(
            user.Id,
            refreshHash,
            familyId ?? SequentialGuid.New(),
            now,
            TimeSpan.FromDays(_jwt.RefreshTokenDays),
            absoluteExpiryUtc ?? now.AddDays(_jwt.RefreshTokenAbsoluteCapDays),
            ipAddress,
            userAgent);

        db.RefreshTokens.Add(refreshToken);
        rotatedFrom?.RotateInto(refreshToken.Id, now, ipAddress);

        await EnforceDeviceCapAsync(user.Id, now, ipAddress, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var profile = await BuildProfileAsync(user, cancellationToken);

        var response = new AuthResponse(accessToken, accessExpiresUtc, refreshToken.ExpiresUtc, profile);

        return (response, refreshPlaintext, refreshToken.ExpiresUtc);
    }

    /// <summary>
    /// Spec §7.2: cap active tokens per user, revoking the oldest beyond the cap. Without
    /// this, every sign-in on a new device accumulates a credential that never goes away.
    /// </summary>
    private async Task EnforceDeviceCapAsync(Guid userId, DateTime now, string? ipAddress, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens
            .AsTracking()
            .Where(t => t.UserId == userId && t.RevokedUtc == null && t.ExpiresUtc > now)
            .OrderByDescending(t => t.CreatedUtc)
            .ThenByDescending(t => t.Id)
            .Skip(_jwt.MaxActiveTokensPerUser)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.Revoke(now, ipAddress, RevocationReasons.DeviceCapExceeded);
        }
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTime now,
        string? ipAddress,
        string reason,
        CancellationToken cancellationToken)
    {
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedUtc, now)
                    .SetProperty(t => t.RevokedByIp, ipAddress)
                    .SetProperty(t => t.RevokedReason, reason),
                cancellationToken);
    }

    /// <summary>
    /// Records an authentication event through Auditing's Contracts. These are non-EF
    /// events, so no interceptor could observe them (spec §9.4).
    /// </summary>
    private void WriteAuthAudit(AuditAction action, Guid? userId, string? userName) =>
        audit.Write(new AuditEntryDto
        {
            Action = action,
            Module = "identity",
            EntityName = "Auth",
            EntityId = userId?.ToString(),
            UserId = userId,
            UserName = userName,
        });

    private async Task<IReadOnlyList<string>> RoleNamesAsync(User user, CancellationToken cancellationToken)
    {
        var roleIds = user.Roles.Select(r => r.RoleId).ToArray();

        return await db.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    private async Task<UserProfileResponse> BuildProfileAsync(User user, CancellationToken cancellationToken)
    {
        var roleNames = await RoleNamesAsync(user, cancellationToken);
        var effective = await permissions.GetPermissionsAsync(user.Id, cancellationToken);

        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.PhoneNumber,
            user.IsActive,
            [.. roleNames],
            [.. effective.Order(StringComparer.Ordinal)]);
    }
}
