using Pinqponq.Contracts;
using Xunit;

namespace Pinqponq.Contracts.Tests;

public sealed class PaginationExtensionsTests
{
    private static readonly int[] TwentyThreeRows = Enumerable.Range(1, 23).ToArray();

    [Fact]
    public void Middle_page_has_its_rows_and_position()
    {
        var response = TwentyThreeRows.ToPagedResponse(page: 2, size: 10, rows => rows);

        Assert.Equal(2, response.Page);
        Assert.Equal(10, response.Size);
        Assert.Equal(23, response.RowCount);
        Assert.Equal(3, response.PageCount);
        Assert.True(response.HasNextPage);
        Assert.Equal(11, response.FirstRowOnPage);
        Assert.Equal(20, response.LastRowOnPage);
        Assert.Equal(Enumerable.Range(11, 10), response.Data);
    }

    [Fact]
    public void Last_partial_page_has_no_next_page()
    {
        var response = TwentyThreeRows.ToPagedResponse(page: 3, size: 10, rows => rows);

        Assert.False(response.HasNextPage);
        Assert.Equal(21, response.FirstRowOnPage);
        Assert.Equal(23, response.LastRowOnPage);
        Assert.Equal([21, 22, 23], response.Data);
    }

    [Fact]
    public void Page_past_the_end_returns_the_last_page()
    {
        var response = TwentyThreeRows.ToPagedResponse(page: 9, size: 10, rows => rows);

        Assert.Equal(3, response.Page);
        Assert.Equal([21, 22, 23], response.Data);
    }

    [Fact]
    public void Exactly_full_pages_end_without_a_next_page()
    {
        var response = Enumerable.Range(1, 20).ToPagedResponse(page: 2, size: 10, rows => rows);

        Assert.Equal(2, response.PageCount);
        Assert.False(response.HasNextPage);
        Assert.Equal(20, response.LastRowOnPage);
    }

    [Fact]
    public void Empty_source_has_no_pages_and_keeps_the_requested_page()
    {
        var response = Array.Empty<int>().ToPagedResponse(page: 4, size: 10, rows => rows.Count);

        Assert.Equal(4, response.Page);
        Assert.Equal(0, response.RowCount);
        Assert.Equal(0, response.PageCount);
        Assert.False(response.HasNextPage);
        Assert.Equal(0, response.FirstRowOnPage);
        Assert.Equal(0, response.LastRowOnPage);
        Assert.Equal(0, response.Data);
    }

    [Fact]
    public void Order_is_applied_before_paging()
    {
        var response = TwentyThreeRows.ToPagedResponse(page: 1, size: 3, rows => rows, orderBy: rows => rows.OrderByDescending(row => row));

        Assert.Equal([23, 22, 21], response.Data);
    }

    [Theory]
    [InlineData(0, 10, "page", "Page number must be greater than 0.")]
    [InlineData(-1, 10, "page", "Page number must be greater than 0.")]
    [InlineData(1, 0, "size", "Page size must be greater than 0.")]
    [InlineData(1, -5, "size", "Page size must be greater than 0.")]
    public void Non_positive_page_or_size_is_rejected(int page, int size, string parameterName, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => TwentyThreeRows.ToPagedResponse(page, size, rows => rows));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.StartsWith(message, exception.Message);
    }

    [Fact]
    public void Missing_source_or_data_factory_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ((int[])null!).ToPagedResponse(1, 10, rows => rows));
        Assert.Throws<ArgumentNullException>(() => TwentyThreeRows.ToPagedResponse<int, List<int>>(1, 10, null!));
    }
}
