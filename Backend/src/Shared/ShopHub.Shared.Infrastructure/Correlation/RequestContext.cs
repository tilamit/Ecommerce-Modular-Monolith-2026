using Microsoft.AspNetCore.Http;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Shared.Infrastructure.Correlation;

/// <summary>
/// Well-known transport headers exchanged with the SPA.
/// </summary>
public static class ShopHubHeaders
{
    /// <summary>Correlates every log line and audit row for one logical request (spec §6.2).</summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>
    /// The SPA route that issued the request, for example <c>/admin/products/edit/{id}</c>.
    /// Recorded as the audit trail's ScreenName (spec §6.6). The server cannot infer this
    /// from the route - the same endpoint is called from several screens.
    /// </summary>
    public const string ClientPage = "X-Client-Page";
}

/// <summary>
/// Scoped snapshot of the current request's transport metadata, for the audit trail.
/// Reading it here rather than reaching for <c>IHttpContextAccessor</c> inside the audit
/// interceptor keeps the interceptor testable without a fake HTTP context.
/// </summary>
internal sealed class RequestAuditContext(IHttpContextAccessor httpContextAccessor) : IAuditContext
{
    private HttpContext? Context => httpContextAccessor.HttpContext;

    public string? ScreenName => Header(ShopHubHeaders.ClientPage);

    public string? CorrelationId => Header(ShopHubHeaders.CorrelationId);

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Header("User-Agent");

    public string? HttpMethod => Context?.Request.Method;

    public string? Path => Context?.Request.Path.Value;

    private string? Header(string name)
    {
        if (Context is null || !Context.Request.Headers.TryGetValue(name, out var values))
        {
            return null;
        }

        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
