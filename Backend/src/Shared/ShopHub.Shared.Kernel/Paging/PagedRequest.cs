namespace ShopHub.Shared.Kernel.Paging;

/// <summary>
/// Query-string shape for every list endpoint (spec §6.5).
/// <c>?page=1&amp;pageSize=20&amp;sort=-createdUtc&amp;search=...</c> - a '-' prefix on
/// <see cref="Sort"/> means descending.
/// </summary>
public sealed record PagedRequest(int Page = 1, int PageSize = PagedRequest.DefaultPageSize, string? Sort = null, string? Search = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MinPageSize = 1;

    /// <summary>
    /// Clamps page and page size into legal ranges (spec §6.5: pageSize to [1,100], page to >= 1).
    /// Done once here rather than per endpoint, so "pageSize=10000" cannot reach a handler.
    /// </summary>
    public PagedRequest Normalized() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = Math.Clamp(PageSize, MinPageSize, MaxPageSize),
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Sort = string.IsNullOrWhiteSpace(Sort) ? null : Sort.Trim(),
    };

    /// <summary>Rows to skip. Only valid on a normalized request.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>True when <see cref="Sort"/> requests descending order.</summary>
    public bool SortDescending => Sort?.StartsWith('-') == true;

    /// <summary>The sort field with any leading '-' removed.</summary>
    public string? SortField => Sort is null ? null : Sort.TrimStart('-');
}
