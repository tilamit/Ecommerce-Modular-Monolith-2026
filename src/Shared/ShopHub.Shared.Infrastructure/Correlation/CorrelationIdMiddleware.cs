using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace ShopHub.Shared.Infrastructure.Correlation;

/// <summary>
/// Reads or creates <c>X-Correlation-Id</c>, pushes it into the Serilog LogContext for the
/// rest of the request, and echoes it on the response (spec §6.2).
/// <para>
/// One id ties together every log line, the audit rows written by the background writer,
/// and the id the SPA can quote in a bug report.
/// </para>
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveCorrelationId(context);

        // Echo before the response starts - once the body is being written, headers are frozen.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ShopHubHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(ShopHubHeaders.CorrelationId, out var incoming))
        {
            var value = incoming.ToString();

            // Cap the length: this value is echoed and logged, so an unbounded client
            // string would let a caller inflate every log line for the request.
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 128)
            {
                return value;
            }
        }

        return Guid.CreateVersion7().ToString("N");
    }
}
