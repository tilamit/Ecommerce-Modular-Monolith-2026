using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.UnitTests.Paging;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(100, 20, 5)]
    public void TotalPages_RoundsUp(long totalCount, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: pageSize, TotalCount: totalCount);

        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void HasNext_IsFalseOnTheLastPage()
    {
        var result = new PagedResult<string>(["a"], Page: 5, PageSize: 20, TotalCount: 100);

        Assert.False(result.HasNext);
        Assert.True(result.HasPrevious);
    }

    [Fact]
    public void HasPrevious_IsFalseOnTheFirstPage()
    {
        var result = new PagedResult<string>(["a"], Page: 1, PageSize: 20, TotalCount: 100);

        Assert.True(result.HasNext);
        Assert.False(result.HasPrevious);
    }

    /// <summary>
    /// A page past the end returns no items rather than an error - the SPA's "back" button
    /// can land there after a filter change and a 500 for that is a bad experience.
    /// </summary>
    [Fact]
    public void PagePastEnd_ReportsNoNextPage()
    {
        var result = new PagedResult<string>([], Page: 99, PageSize: 20, TotalCount: 10);

        Assert.Empty(result.Items);
        Assert.False(result.HasNext);
    }
}
