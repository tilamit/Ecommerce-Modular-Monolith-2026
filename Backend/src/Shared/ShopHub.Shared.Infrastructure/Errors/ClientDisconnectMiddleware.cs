using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShopHub.Shared.Infrastructure.Errors;

/// <summary>
/// Absorbs the cancellation that follows a client hanging up mid-request.
/// <para>
/// Request tokens are <see cref="HttpContext.RequestAborted"/>, so an abandoned request (a
/// navigation, a superseded typeahead call, React's development double mount) makes EF Core,
/// HybridCache and HttpClient throw <see cref="OperationCanceledException"/>. There is no
/// fault to report and nobody left to read a ProblemDetails body.
/// </para>
/// <para>
/// Handling it in <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> produces
/// the right response but lets the exception escape into framework code, which the Visual
/// Studio debugger reports as "Exception User-Unhandled" on an ordinary page load. Catching
/// it here, inside <c>UseExceptionHandler</c>, keeps it in first-party frames.
/// </para>
/// </summary>
internal sealed class ClientDisconnectMiddleware(
    RequestDelegate next,
    ILogger<ClientDisconnectMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);

            // Not every hang-up throws. A read served from HybridCache deliberately runs to
            // completion regardless of the caller (see GetOrCreateSharedAsync), so an
            // abandoned request can reach here having succeeded, with nobody left to read
            // the body. Recording that as 200 would overstate what happened, so the same
            // 499 is applied on the way out.
            if (context.RequestAborted.IsCancellationRequested)
            {
                RecordHangUp(context);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            RecordHangUp(context);
        }
    }

    private void RecordHangUp(HttpContext context)
    {
        // Debug, not Warning: a shopper who clicks through three products in two seconds is
        // normal traffic and a louder level would bury everything else in the log.
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Client disconnected during {Method} {Path}; the request was abandoned.",
                context.Request.Method,
                context.Request.Path);
        }

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ShopHubStatusCodes.ClientClosedRequest;
        }
    }
}

public static class ClientDisconnectMiddlewareExtensions
{
    /// <summary>
    /// Register immediately inside <c>UseExceptionHandler</c>, so a client hang-up is
    /// resolved here rather than travelling out through the exception pipeline.
    /// </summary>
    public static IApplicationBuilder UseClientDisconnectHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<ClientDisconnectMiddleware>();
    }
}
