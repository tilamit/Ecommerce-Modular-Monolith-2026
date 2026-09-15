namespace ShopHub.Shared.Kernel.Results;

/// <summary>
/// The category of a failure. Maps 1:1 onto the HTTP status table in spec §6.1,
/// which is what lets a handler return a <see cref="Result"/> without knowing about HTTP.
/// </summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    DomainRule,
    Unexpected,
}

/// <summary>
/// A failure with a stable machine-readable <paramref name="Code"/> and a human message.
/// The code is what clients branch on (for example <c>refresh_expired</c> in spec §7.3).
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Unexpected);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error DomainRule(string code, string message) => new(code, message, ErrorType.DomainRule);

    public static Error Unexpected(string code, string message) => new(code, message, ErrorType.Unexpected);
}
