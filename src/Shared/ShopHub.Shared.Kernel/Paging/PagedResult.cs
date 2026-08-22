namespace ShopHub.Shared.Kernel.Paging;

/// <summary>
/// Envelope returned by every offset-paged list endpoint (spec §6.5).
/// No endpoint anywhere returns an unbounded collection - including dropdowns.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNext => Page < TotalPages;

    public bool HasPrevious => Page > 1;
}

/// <summary>
/// Envelope for keyset-paginated endpoints (spec §6.5). Used where a table grows without
/// bound and offset paging degrades - the audit trail uses this from the start.
/// <paramref name="NextCursor"/> is opaque to the client; null means the end of the set.
/// </summary>
public sealed record CursorResult<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public bool HasMore => NextCursor is not null;
}
