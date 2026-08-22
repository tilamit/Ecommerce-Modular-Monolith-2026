namespace ShopHub.Shared.Kernel.Exceptions;

/// <summary>
/// Base for exceptions the API knows how to translate into a ProblemDetails response
/// (spec §6.1). Anything not deriving from this is a 500 with a generic message.
/// </summary>
public abstract class ShopHubException : Exception
{
    protected ShopHubException(string code, string message)
        : base(message)
        => Code = code;

    protected ShopHubException(string code, string message, Exception innerException)
        : base(message, innerException)
        => Code = code;

    /// <summary>Stable machine-readable code surfaced to the client.</summary>
    public string Code { get; }
}

/// <summary>404. The requested resource does not exist, or the caller may not know that it does.</summary>
public sealed class NotFoundException : ShopHubException
{
    public NotFoundException(string message)
        : base("not_found", message)
    {
    }

    public NotFoundException(string code, string message)
        : base(code, message)
    {
    }

    public static NotFoundException For(string entityName, object id) =>
        new($"{entityName} '{id}' was not found.");
}

/// <summary>409. The request conflicts with current state (duplicate key, concurrency loss).</summary>
public sealed class ConflictException : ShopHubException
{
    public ConflictException(string message)
        : base("conflict", message)
    {
    }

    public ConflictException(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>
/// 401. The caller is not authenticated, or their session can no longer be renewed.
/// <para>
/// Distinct from <see cref="ForbiddenException"/> on purpose: 401 means "authenticate and
/// try again", 403 means "you are authenticated and still may not". Spec §7.3 requires the
/// refresh endpoint to answer 401 with a distinguishable code so the SPA redirects to login
/// instead of looping on refresh.
/// </para>
/// </summary>
public sealed class UnauthorizedException : ShopHubException
{
    public UnauthorizedException(string message)
        : base("unauthorized", message)
    {
    }

    public UnauthorizedException(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>403. The caller is authenticated but lacks the required permission or ownership.</summary>
public sealed class ForbiddenException : ShopHubException
{
    public ForbiddenException(string message)
        : base("forbidden", message)
    {
    }

    public ForbiddenException(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>
/// 422. A business rule was violated - the request was well-formed and the caller was
/// allowed, but the domain refused it (for example, cancelling a shipped order).
/// </summary>
public sealed class DomainRuleException : ShopHubException
{
    public DomainRuleException(string message)
        : base("domain_rule_violated", message)
    {
    }

    public DomainRuleException(string code, string message)
        : base(code, message)
    {
    }
}
