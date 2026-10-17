namespace Pinqponq.Contracts;

/// <summary>
/// Describes which rows belong to one requested page. It is computed from the page number, the page
/// size and the total number of rows. To use it: read the page from your database with
/// <see cref="Skip"/> and <see cref="Size"/>, then pass the rows to <see cref="ToResponse{TEntity, TData}"/>.
/// </summary>
public readonly struct PageWindow
{
    private PageWindow(int page, int size, int rowCount, int pageCount, int skip)
    {
        Page = page;
        Size = size;
        RowCount = rowCount;
        PageCount = pageCount;
        Skip = skip;
    }

    /// <summary>One-based page number the response reports.</summary>
    public int Page { get; }

    /// <summary>Page size after the <see cref="PaginationOptions.MaxPageSize"/> cap.</summary>
    public int Size { get; }

    /// <summary>Total number of rows across all pages.</summary>
    public int RowCount { get; }

    /// <summary>Total number of pages; 0 when there are no rows.</summary>
    public int PageCount { get; }

    /// <summary>Number of rows before this page.</summary>
    public int Skip { get; }

    /// <summary>
    /// Rejects a page number or page size under 1. Call it before counting rows, so an invalid
    /// request does not cost a query.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The message is <see cref="CommonErrorCodes.ApiPageSizeInvalid"/> or
    /// <see cref="CommonErrorCodes.ApiPageNumberInvalid"/>, so Pinqponq.ErrorHandling's localized mode answers with that code.
    /// </exception>
    public static void ValidateRequest(int page, int size)
    {
        if (size <= 0) throw new ArgumentException(CommonErrorCodes.ApiPageSizeInvalid);
        if (page <= 0) throw new ArgumentException(CommonErrorCodes.ApiPageNumberInvalid);
    }

    /// <summary>
    /// Resolves the window of <paramref name="page"/> over <paramref name="rowCount"/> rows.
    /// </summary>
    /// <exception cref="ArgumentException">See <see cref="ValidateRequest"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rowCount"/> is negative.</exception>
    /// <exception cref="OverflowException"><paramref name="rowCount"/> does not fit the response's <c>int</c> row count.</exception>
    public static PageWindow Resolve(int page, int size, long rowCount, PaginationOptions? options = null)
    {
        ValidateRequest(page, size);
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        var paginationOptions = options ?? PaginationOptions.Default;

        var pageSize = Math.Min(size, paginationOptions.MaxPageSize);
        var totalRowCount = checked((int)rowCount);
        var pageCount = (int)(((long)totalRowCount + pageSize - 1) / pageSize);
        var isLastPageCap = pageCount > 0 && paginationOptions.PastLastPage == PastLastPageBehavior.ReturnLastPage;
        var returnedPage = isLastPageCap ? Math.Min(page, pageCount) : page;
        var skip = (int)Math.Min((long)(returnedPage - 1) * pageSize, totalRowCount);

        return new PageWindow(returnedPage, pageSize, totalRowCount, pageCount, skip);
    }

    /// <summary>
    /// Wraps the rows of this window in a <see cref="PagedResponseModel{T}"/>.
    /// </summary>
    public PagedResponseModel<TData> ToResponse<TEntity, TData>(List<TEntity> pageRows, Func<List<TEntity>, TData> dataFactory)
    {
        ArgumentNullException.ThrowIfNull(pageRows);
        ArgumentNullException.ThrowIfNull(dataFactory);

        var data = dataFactory(pageRows);
        var hasRowsOnPage = pageRows.Count > 0;
        var firstRowOnPage = hasRowsOnPage ? Skip + 1 : 0;
        var lastRowOnPage = hasRowsOnPage ? Skip + pageRows.Count : 0;
        var hasNextPage = Page < PageCount;

        return new PagedResponseModel<TData>
        {
            Page = Page,
            Size = Size,
            RowCount = RowCount,
            PageCount = PageCount,
            HasNextPage = hasNextPage,
            FirstRowOnPage = firstRowOnPage,
            LastRowOnPage = lastRowOnPage,
            Data = data,
        };
    }
}
