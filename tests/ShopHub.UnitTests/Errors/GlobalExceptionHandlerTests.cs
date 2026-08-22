using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ShopHub.Shared.Infrastructure.Errors;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.UnitTests.Errors;

/// <summary>
/// Spec §6.1: "Never leak exception messages, stack traces, SQL, or connection details to
/// the client in non-Development environments."
/// <para>
/// This is asserted here rather than in an integration test because the diagnostics
/// endpoint that throws on demand is mapped in Development only - so the environment that
/// most needs checking is exactly the one that endpoint cannot reach.
/// </para>
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    private const string LeakySecret = "Server=prod-sql-01;Password=hunter2;Initial Catalog=ShopHub";

    [Fact]
    public async Task Production_DoesNotLeakExceptionDetail()
    {
        var (handler, context, captured) = CreateHandler(Environments.Production);

        await handler.TryHandleAsync(context, new InvalidOperationException(LeakySecret), CancellationToken.None);

        Assert.NotNull(captured.ProblemDetails);
        Assert.Equal(StatusCodes.Status500InternalServerError, captured.ProblemDetails!.Status);
        Assert.DoesNotContain(LeakySecret, captured.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", captured.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_IncludesExceptionDetailForDebugging()
    {
        var (handler, context, captured) = CreateHandler(Environments.Development);

        await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.Contains("InvalidOperationException", captured.ProblemDetails!.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 4xx carries its own message in every environment - it describes the caller's
    /// mistake, not the server's internals.
    /// </summary>
    [Fact]
    public async Task Production_StillExplainsClientErrors()
    {
        var (handler, context, captured) = CreateHandler(Environments.Production);

        await handler.TryHandleAsync(context, new NotFoundException("Product 'abc' was not found."), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, captured.ProblemDetails!.Status);
        Assert.Equal("Product 'abc' was not found.", captured.ProblemDetails.Detail);
        Assert.Equal("not_found", captured.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task EveryResponse_CarriesATraceId()
    {
        var (handler, context, captured) = CreateHandler(Environments.Production);

        await handler.TryHandleAsync(context, new ConflictException("duplicate"), CancellationToken.None);

        Assert.True(captured.ProblemDetails!.Extensions.TryGetValue("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId as string));
    }

    private static (GlobalExceptionHandler Handler, HttpContext Context, CapturingProblemDetailsService Captured) CreateHandler(
        string environmentName)
    {
        var captured = new CapturingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            captured,
            new StubHostEnvironment { EnvironmentName = environmentName },
            NullLogger<GlobalExceptionHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/products/abc";

        return (handler, context, captured);
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetails? ProblemDetails { get; private set; }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            ProblemDetails = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            ProblemDetails = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "ShopHub.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
