using System.Linq.Expressions;
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

    [Fact]
    public void Page_past_the_end_returns_an_empty_page_when_the_service_asks_for_it()
    {
        var emptyPageOptions = new PaginationOptions { PastLastPage = PastLastPageBehavior.ReturnEmptyPage };

        var response = TwentyThreeRows.ToPagedResponse(page: 9, size: 10, rows => rows, options: emptyPageOptions);

        Assert.Equal(9, response.Page);
        Assert.Equal(23, response.RowCount);
        Assert.Equal(3, response.PageCount);
        Assert.False(response.HasNextPage);
        Assert.Equal(0, response.FirstRowOnPage);
        Assert.Equal(0, response.LastRowOnPage);
        Assert.Empty(response.Data);
    }

    [Fact]
    public void Page_size_above_the_maximum_is_lowered_to_it()
    {
        var response = Enumerable.Range(1, 150).ToPagedResponse(page: 1, size: 500, rows => rows);

        Assert.Equal(PaginationOptions.DEFAULT_MAX_PAGE_SIZE, response.Size);
        Assert.Equal(2, response.PageCount);
        Assert.Equal(100, response.Data.Count);
    }

    [Fact]
    public void Query_is_counted_and_only_the_requested_page_is_read()
    {
        var query = TwentyThreeRows.AsQueryable().OrderByDescending(row => row);

        var response = query.ToPagedResponse(page: 2, size: 10, rows => rows);

        Assert.Equal(23, response.RowCount);
        Assert.Equal(3, response.PageCount);
        Assert.True(response.HasNextPage);
        Assert.Equal(11, response.FirstRowOnPage);
        Assert.Equal(20, response.LastRowOnPage);
        Assert.Equal(Enumerable.Range(4, 10).Reverse(), response.Data);
    }

    [Fact]
    public void Query_page_past_the_end_follows_the_options()
    {
        var query = TwentyThreeRows.AsQueryable();
        var emptyPageOptions = new PaginationOptions { PastLastPage = PastLastPageBehavior.ReturnEmptyPage };

        var lastPage = query.ToPagedResponse(page: 5, size: 10, rows => rows);
        var emptyPage = query.ToPagedResponse(page: 5, size: 10, rows => rows, emptyPageOptions);

        Assert.Equal(3, lastPage.Page);
        Assert.Equal([21, 22, 23], lastPage.Data);
        Assert.Equal(5, emptyPage.Page);
        Assert.Empty(emptyPage.Data);
    }

    [Theory]
    [InlineData(0, 10, CommonErrorCodes.ApiPageNumberInvalid)]
    [InlineData(1, 0, CommonErrorCodes.ApiPageSizeInvalid)]
    public void Invalid_query_request_is_rejected_before_the_query_runs(int page, int size, string expectedResponseCode)
    {
        var query = new ThrowingQueryProvider().CreateQuery<int>(Expression.Constant(TwentyThreeRows.AsQueryable()));

        var exception = Assert.Throws<ArgumentException>(() => query.ToPagedResponse(page, size, rows => rows));

        Assert.Equal(expectedResponseCode, exception.Message);
    }

    [Fact]
    public void Missing_query_is_rejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ((IQueryable<int>)null!).ToPagedResponse(1, 10, rows => rows));

        Assert.Equal("query", exception.ParamName);
    }

    [Fact]
    public void Missing_data_factory_is_rejected_before_the_query_runs()
    {
        var query = new ThrowingQueryProvider().CreateQuery<int>(Expression.Constant(TwentyThreeRows.AsQueryable()));

        var exception = Assert.Throws<ArgumentNullException>(() => query.ToPagedResponse<int, List<int>>(1, 10, null!));

        Assert.Equal("dataFactory", exception.ParamName);
    }

    [Theory]
    [InlineData(0, 10, CommonErrorCodes.ApiPageNumberInvalid)]
    [InlineData(-1, 10, CommonErrorCodes.ApiPageNumberInvalid)]
    [InlineData(1, 0, CommonErrorCodes.ApiPageSizeInvalid)]
    [InlineData(1, -5, CommonErrorCodes.ApiPageSizeInvalid)]
    public void Non_positive_page_or_size_is_rejected_with_its_response_code_as_the_message(int page, int size, string expectedResponseCode)
    {
        var exception = Assert.Throws<ArgumentException>(() => TwentyThreeRows.ToPagedResponse(page, size, rows => rows));

        Assert.Equal(expectedResponseCode, exception.Message);
    }

    [Fact]
    public void Missing_source_is_rejected_before_ordering()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ((int[])null!).ToPagedResponse(1, 10, rows => rows, orderBy: _ => throw new InvalidOperationException("The source must not be ordered.")));

        Assert.Equal("source", exception.ParamName);
    }

    [Fact]
    public void Missing_data_factory_is_rejected_before_the_source_is_read()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new UnreadableRows().ToPagedResponse<int, List<int>>(1, 10, null!));

        Assert.Equal("dataFactory", exception.ParamName);
    }

    [Fact]
    public void Invalid_request_is_rejected_before_the_source_is_read()
    {
        var exception = Assert.Throws<ArgumentException>(() => new UnreadableRows().ToPagedResponse(0, 10, rows => rows));

        Assert.Equal(CommonErrorCodes.ApiPageNumberInvalid, exception.Message);
    }

    private sealed class UnreadableRows : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => throw new InvalidOperationException("The source must not be read.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ThrowingQueryProvider : IQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => throw new NotSupportedException();

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new ThrowingQuery<TElement>(this, expression);

        public object Execute(Expression expression) => throw new InvalidOperationException("The query must not run.");

        public TResult Execute<TResult>(Expression expression) => throw new InvalidOperationException("The query must not run.");
    }

    private sealed class ThrowingQuery<TElement>(IQueryProvider provider, Expression expression) : IQueryable<TElement>
    {
        public Type ElementType => typeof(TElement);

        public Expression Expression => expression;

        public IQueryProvider Provider => provider;

        public IEnumerator<TElement> GetEnumerator() => throw new InvalidOperationException("The query must not run.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
