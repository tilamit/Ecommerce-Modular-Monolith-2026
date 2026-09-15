using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ShopHub.Shared.Infrastructure.Security;

/// <summary>
/// Response security headers (spec §14 Phase 11).
/// </summary>
/// <remarks>
/// This is an API, not an HTML app, which changes what is worth sending. There is no markup
/// to protect from injection here, so the headers that matter are the ones that stop a
/// browser from <em>treating</em> a JSON response as something else, or from leaking where a
/// request came from.
/// </remarks>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Set on OnStarting so they survive short-circuiting middleware - a 429 from the
        // rate limiter or a 401 challenge gets the same headers as a 200.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Stops a browser MIME-sniffing a JSON body into script. The single most
            // valuable header for an API.
            headers["X-Content-Type-Options"] = "nosniff";

            // Nothing here is meant to be framed. DENY rather than SAMEORIGIN because the
            // API serves no UI of its own.
            headers["X-Frame-Options"] = "DENY";

            // Send the origin to other sites, the full URL to our own: an API path can
            // carry ids that do not belong in another site's logs.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // The API needs none of these; denying them is free.
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

            // A CSP that forbids everything. The API returns JSON, so no resource of any
            // kind should ever be loaded from one of its responses - and if a browser is
            // rendering one directly, that is already a bug worth containing.
            //
            // Assigned only if nothing upstream has set one. The Development-only API
            // reference is a real HTML page served from this same host and a blanket
            // `default-src 'none'; sandbox` renders it as a blank screen; it sets its own
            // policy and this must not overwrite it. Every other response still gets this.
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; sandbox";
            }

            // Cross-origin isolation: neither embeddable nor readable from another origin.
            headers["Cross-Origin-Resource-Policy"] = "same-site";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // A reverse proxy in front of Kestrel may add these. Kestrel's own Server
            // header is switched off at the host level instead (AddServerHeader = false),
            // because it is written below this pipeline and cannot be removed from here -
            // verified by observing it survive this middleware.
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await next(context);
    }
}

public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adds the response security headers. Register early, so a response short-circuited
    /// further down the pipeline still carries them.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }

    /// <summary>
    /// HSTS outside Development (spec §14 Phase 11).
    /// <para>
    /// Deliberately not enabled in Development: the dev host runs on plain HTTP and an
    /// HSTS header cached by the browser for <c>localhost</c> is remarkably annoying to
    /// undo across every project on the machine.
    /// </para>
    /// </summary>
    public static IServiceCollection AddShopHubHsts(this IServiceCollection services)
    {
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        return services;
    }
}
