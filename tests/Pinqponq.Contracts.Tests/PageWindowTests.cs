using Pinqponq.Contracts;
using Xunit;

namespace Pinqponq.Contracts.Tests;

public sealed class PageWindowTests
{
    private static readonly PaginationOptions EmptyPageOptions = new() { PastLastPage = PastLastPageBehavior.ReturnEmptyPage };

    [Fact]
    public void Window_skips_the_rows_of_the_earlier_pages()
    {
        var window = PageWindow.Resolve(page: 3, size: 20, rowCount: 95);

        Assert.Equal(3, window.Page);
        Assert.Equal(20, window.Size);
        Assert.Equal(95, window.RowCount);
        Assert.Equal(5, window.PageCount);
        Assert.Equal(40, window.Skip);
    }

    [Fact]
    public void Service_maximum_replaces_the_default_maximum()
    {
        var servicePagingOptions = new PaginationOptions { MaxPageSize = 200 };

        var window = PageWindow.Resolve(page: 1, size: 500, rowCount: 1_000, servicePagingOptions);

        Assert.Equal(200, window.Size);
        Assert.Equal(5, window.PageCount);
    }

    [Fact]
    public void Page_size_at_the_maximum_is_kept()
    {
        var window = PageWindow.Resolve(page: 1, size: PaginationOptions.DEFAULT_MAX_PAGE_SIZE, rowCount: 1_000);

        Assert.Equal(PaginationOptions.DEFAULT_MAX_PAGE_SIZE, window.Size);
    }

    [Fact]
    public void Page_past_the_end_moves_to_the_last_page_by_default()
    {
        var window = PageWindow.Resolve(page: 7, size: 10, rowCount: 23);

        Assert.Equal(3, window.Page);
        Assert.Equal(20, window.Skip);
    }

    [Fact]
    public void Empty_page_past_the_end_skips_every_row()
    {
        var window = PageWindow.Resolve(page: 7, size: 10, rowCount: 23, EmptyPageOptions);

        Assert.Equal(7, window.Page);
        Assert.Equal(23, window.Skip);
    }

    [Fact]
    public void Very_large_page_number_does_not_overflow_the_skip()
    {
        var window = PageWindow.Resolve(page: int.MaxValue, size: 100, rowCount: 23, EmptyPageOptions);

        Assert.Equal(int.MaxValue, window.Page);
        Assert.Equal(23, window.Skip);
    }

    [Fact]
    public void No_rows_keeps_the_requested_page_with_either_behavior()
    {
        var lastPageWindow = PageWindow.Resolve(page: 4, size: 10, rowCount: 0);
        var emptyPageWindow = PageWindow.Resolve(page: 4, size: 10, rowCount: 0, EmptyPageOptions);

        Assert.Equal(4, lastPageWindow.Page);
        Assert.Equal(0, lastPageWindow.PageCount);
        Assert.Equal(0, lastPageWindow.Skip);
        Assert.Equal(4, emptyPageWindow.Page);
        Assert.Equal(0, emptyPageWindow.Skip);
    }

    [Fact]
    public void Response_reports_the_rows_of_the_window()
    {
        var window = PageWindow.Resolve(page: 2, size: 10, rowCount: 23);

        var response = window.ToResponse([11, 12, 13], rows => rows.Sum());

        Assert.Equal(2, response.Page);
        Assert.Equal(10, response.Size);
        Assert.Equal(23, response.RowCount);
        Assert.Equal(3, response.PageCount);
        Assert.True(response.HasNextPage);
        Assert.Equal(11, response.FirstRowOnPage);
        Assert.Equal(13, response.LastRowOnPage);
        Assert.Equal(36, response.Data);
    }

    [Fact]
    public void Response_of_the_last_page_has_no_next_page()
    {
        var window = PageWindow.Resolve(page: 3, size: 10, rowCount: 23);

        var response = window.ToResponse([21, 22, 23], rows => rows);

        Assert.False(response.HasNextPage);
        Assert.Equal(21, response.FirstRowOnPage);
        Assert.Equal(23, response.LastRowOnPage);
    }

    [Fact]
    public void Response_without_rows_has_no_row_positions()
    {
        var window = PageWindow.Resolve(page: 7, size: 10, rowCount: 23, EmptyPageOptions);

        var response = window.ToResponse(new List<int>(), rows => rows);

        Assert.False(response.HasNextPage);
        Assert.Equal(0, response.FirstRowOnPage);
        Assert.Equal(0, response.LastRowOnPage);
        Assert.Empty(response.Data);
    }

    [Theory]
    [InlineData(0, 10, CommonErrorCodes.ApiPageNumberInvalid)]
    [InlineData(1, 0, CommonErrorCodes.ApiPageSizeInvalid)]
    [InlineData(0, 0, CommonErrorCodes.ApiPageSizeInvalid)]
    public void Invalid_request_is_rejected_with_its_response_code_as_the_message(int page, int size, string expectedResponseCode)
    {
        var exception = Assert.Throws<ArgumentException>(() => PageWindow.Resolve(page, size, rowCount: 23));

        Assert.Equal(expectedResponseCode, exception.Message);
    }

    [Fact]
    public void Negative_row_count_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageWindow.Resolve(page: 1, size: 10, rowCount: -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Non_positive_maximum_is_rejected_when_the_options_are_built(int maxPageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginationOptions { MaxPageSize = maxPageSize });
    }

    [Fact]
    public void Row_count_above_the_response_range_is_rejected()
    {
        Assert.Throws<OverflowException>(() => PageWindow.Resolve(page: 1, size: 10, rowCount: (long)int.MaxValue + 1));
    }

    [Fact]
    public void Missing_rows_or_data_factory_is_rejected()
    {
        var window = PageWindow.Resolve(page: 1, size: 10, rowCount: 23);

        Assert.Throws<ArgumentNullException>(() => window.ToResponse<int, List<int>>(null!, rows => rows));
        Assert.Throws<ArgumentNullException>(() => window.ToResponse<int, List<int>>([1], null!));
    }
}
