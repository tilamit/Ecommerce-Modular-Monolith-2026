namespace ShopHub.Shared.Kernel.Abstractions;

/// <summary>
/// The authenticated caller for the current request, resolved from the JWT.
/// Ownership rules (spec §7.5) are enforced against <see cref="Id"/> - never against a
/// client-supplied id.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Null for anonymous callers (public storefront, guest checkout).</summary>
    Guid? Id { get; }

    string? Email { get; }

    string? Name { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// The caller's id, or throws when anonymous. Use on endpoints that already
    /// require authentication, so the null check does not repeat in every handler.
    /// </summary>
    Guid RequiredId { get; }
}

/// <summary>
/// Per-request context captured from transport headers for the audit trail (spec §6.6).
/// <see cref="ScreenName"/> comes from the SPA's <c>X-Client-Page</c> header - the same
/// endpoint is called from several screens, so it cannot be inferred from the route.
/// </summary>
public interface IAuditContext
{
    string? ScreenName { get; }

    string? CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? HttpMethod { get; }

    string? Path { get; }
}
