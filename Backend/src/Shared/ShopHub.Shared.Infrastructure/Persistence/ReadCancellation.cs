namespace ShopHub.Shared.Infrastructure.Persistence;

/// <summary>
/// The token to use for a short, paged, read-only query that should finish rather than be
/// abandoned when its caller goes away.
/// </summary>
/// <remarks>
/// <para>
/// Threading <c>HttpContext.RequestAborted</c> into a query is right when abandoning the
/// work saves something. For a paged list read it does not: the page size is bounded, the
/// query is already capped by the database command timeout and the most a cancellation can
/// reclaim is one round trip that is usually already in flight.
/// </para>
/// <para>
/// What it costs is not free. React's development double-mount cancels and re-issues the
/// first request of every grid, so an ordinary page load raised
/// <c>OperationCanceledException</c> out of EF Core in first-party code. That is a
/// user-unhandled break in the debugger on a screen where nothing is wrong and it cannot be
/// silenced by catching further out, because the debugger decides at the moment of the throw
/// and by then the middleware's frame is a continuation rather than a caller.
/// </para>
/// <para>
/// Note this is a different argument from the one behind
/// <c>HybridCacheExtensions.GetOrCreateSharedAsync</c>. There the factory is not cancellable
/// because the work is <em>shared</em> and no single caller owns it. Here the work is
/// per-caller and genuinely abandonable; it is simply not worth abandoning. Both end up
/// passing no token, for reasons worth keeping apart.
/// </para>
/// <para>
/// This is for reads only. A write, a long report or anything unbounded should still take
/// the caller's token.
/// </para>
/// </remarks>
public static class ReadCancellation
{
    /// <summary>
    /// <see cref="CancellationToken.None"/>, named so a call site says why it is passing
    /// nothing rather than looking like an oversight.
    /// </summary>
    public static CancellationToken RunToCompletion => CancellationToken.None;
}
