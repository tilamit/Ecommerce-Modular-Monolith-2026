using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Shared.Infrastructure.Errors;

/// <summary>
/// Translates exceptions into RFC 9457 ProblemDetails (spec §6.1).
/// <para>
/// Two rules drive everything here. First, never leak internals: outside Development the
/// client gets a sanitized shape and the detail goes to the log. Second, log level follows
/// status - 4xx is a Warning, 5xx is an Error - because logging expected validation
/// failures at Error destroys the signal in the log.
/// </para>
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, code, errors) = Map(exception);
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path}. TraceId {TraceId}.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                traceId);
        }
        else
        {
            logger.LogWarning(
                "Request failed with {Status} ({Code}) on {Method} {Path}. TraceId {TraceId}. {Message}",
                status,
                code,
                httpContext.Request.Method,
                httpContext.Request.Path,
                traceId,
                exception.Message);
        }

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["code"] = code;

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        // A 500's message may contain SQL, connection details, or file paths. Only
        // Development sees it; everyone else gets the title and the traceId to quote.
        if (status >= StatusCodes.Status500InternalServerError)
        {
            problemDetails.Detail = environment.IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred. Quote the traceId when reporting this.";
        }
        else
        {
            problemDetails.Detail = exception.Message;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });
    }

    /// <summary>
    /// Nginx's 499, used when the client disconnects mid-request. Not present in
    /// <see cref="StatusCodes"/>, and worth naming rather than writing 499 inline.
    /// </summary>
    private const int ClientClosedRequest = 499;

    private static (int Status, string Title, string Code, IDictionary<string, string[]>? Errors) Map(Exception exception) =>
        exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                "One or more validation errors occurred.",
                "validation_failed",
                validation.Errors
                    .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray(),
                        StringComparer.Ordinal)),

            NotFoundException notFound => (
                StatusCodes.Status404NotFound, "Resource not found.", notFound.Code, null),

            ConflictException conflict => (
                StatusCodes.Status409Conflict, "The request conflicts with the current state.", conflict.Code, null),

            // Optimistic concurrency (spec §6.7: rowversion on Products, Orders, Users).
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "The record was modified by someone else. Reload and try again.",
                "concurrency_conflict",
                null),

            ForbiddenException forbidden => (
                StatusCodes.Status403Forbidden, "You do not have access to this resource.", forbidden.Code, null),

            DomainRuleException domainRule => (
                StatusCodes.Status422UnprocessableEntity, "A business rule was violated.", domainRule.Code, null),

            OperationCanceledException => (
                ClientClosedRequest, "The request was cancelled.", "request_cancelled", null),

            _ => (
                StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "internal_error", null),
        };
}
