using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ShopHub.Shared.Infrastructure.Security;

/// <summary>
/// Requirement carrying the permission code an endpoint asked for.
/// </summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Resolves a user's effective permissions. Implemented by the Identity module, which owns
/// the tables; declared here so <see cref="PermissionAuthorizationHandler"/> can depend on
/// the capability without Shared.Infrastructure referencing a module (the architecture
/// tests forbid that and rightly).
/// </summary>
public interface IUserPermissionResolver
{
    /// <summary>
    /// Effective permission codes for a user, via their roles. Implementations are expected
    /// to cache (spec §7.5: <c>identity:permissions:user:{id}</c>, 10 min, invalidated on
    /// role or permission change).
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Grants access when the caller's resolved permissions contain the required code.
/// <para>
/// Permissions are looked up server-side rather than read from the token, because a
/// 15-minute access token would otherwise keep serving permissions that an admin has
/// already revoked (spec §7.1).
/// </para>
/// </summary>
internal sealed class PermissionAuthorizationHandler(IUserPermissionResolver resolver, Kernel.Abstractions.ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var userId = currentUser.Id;

        if (userId is null)
        {
            // Anonymous: leave the requirement unmet. Failing outright here would
            // short-circuit other handlers that might legitimately satisfy it.
            return;
        }

        var permissions = await resolver.GetPermissionsAsync(userId.Value, CancellationToken.None);

        if (permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>
/// Creates an authorization policy on demand for any permission string, so
/// <c>.RequireAuthorization(Permissions.ProductsWrite)</c> works without registering a
/// named policy per permission at startup.
/// </summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // An explicitly registered policy always wins, so this provider can coexist with
        // conventional named policies.
        var configured = await _fallback.GetPolicyAsync(policyName);

        if (configured is not null)
        {
            return configured;
        }

        // A permission code, by convention, contains a dot: "catalog.products.write".
        // Anything else is not ours - returning null lets the framework report it as an
        // unknown policy rather than silently authorising.
        if (!policyName.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}
