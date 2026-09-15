using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.UnitTests.Paging;

/// <summary>
/// Spec §6.5: clamping happens in one shared place, not per endpoint. These are the
/// pagination edge cases §12 calls out - page 0, an oversized page size and page past end.
/// </summary>
public sealed class PagedRequestTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Normalized_ClampsPageToAtLeastOne(int requested, int expected)
    {
        var result = new PagedRequest(Page: requested).Normalized();

        Assert.Equal(expected, result.Page);
    }

    [Theory]
    [InlineData(10000, PagedRequest.MaxPageSize)]
    [InlineData(101, PagedRequest.MaxPageSize)]
    [InlineData(0, PagedRequest.MinPageSize)]
    [InlineData(-1, PagedRequest.MinPageSize)]
    [InlineData(20, 20)]
    public void Normalized_ClampsPageSizeIntoRange(int requested, int expected)
    {
        var result = new PagedRequest(PageSize: requested).Normalized();

        Assert.Equal(expected, result.PageSize);
    }

    [Fact]
    public void Normalized_TreatsBlankSearchAndSortAsAbsent()
    {
        var result = new PagedRequest(Search: "   ", Sort: "  ").Normalized();

        Assert.Null(result.Search);
        Assert.Null(result.Sort);
    }

    [Fact]
    public void Normalized_TrimsSearchTerm()
    {
        var result = new PagedRequest(Search: "  laptop  ").Normalized();

        Assert.Equal("laptop", result.Search);
    }

    [Theory]
    [InlineData("-createdUtc", true, "createdUtc")]
    [InlineData("price", false, "price")]
    [InlineData(null, false, null)]
    public void SortPrefix_SelectsDirectionAndField(string? sort, bool expectedDescending, string? expectedField)
    {
        var request = new PagedRequest(Sort: sort).Normalized();

        Assert.Equal(expectedDescending, request.SortDescending);
        Assert.Equal(expectedField, request.SortField);
    }

    [Fact]
    public void Skip_IsZeroOnTheFirstPage()
    {
        var request = new PagedRequest(Page: 1, PageSize: 20).Normalized();

        Assert.Equal(0, request.Skip);
    }

    [Fact]
    public void Skip_AdvancesByPageSize()
    {
        var request = new PagedRequest(Page: 3, PageSize: 20).Normalized();

        Assert.Equal(40, request.Skip);
    }
}
