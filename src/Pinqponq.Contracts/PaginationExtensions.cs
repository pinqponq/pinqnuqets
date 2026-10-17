namespace Pinqponq.Contracts;

/// <summary>
/// Builds a <see cref="PagedResponseModel{T}"/> from an in-memory sequence.
/// </summary>
public static class PaginationExtensions
{
    /// <summary>
    /// Orders <paramref name="source"/> (optionally), takes the requested page and wraps it.
    /// A page number past the last page returns the last page.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The page number or the page size is not positive; the message is <see cref="CommonErrorCodes.ApiPageNumberInvalid"/>
    /// or <see cref="CommonErrorCodes.ApiPageSizeInvalid"/>, so Pinqponq.ErrorHandling's localized mode answers with that code.
    /// </exception>
    public static PagedResponseModel<TData> ToPagedResponse<TEntity, TData>(
        this IEnumerable<TEntity> source,
        int page,
        int size,
        Func<List<TEntity>, TData> dataFactory,
        Func<IEnumerable<TEntity>, IEnumerable<TEntity>>? orderBy = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dataFactory);
        if (size <= 0) throw new ArgumentException(CommonErrorCodes.ApiPageSizeInvalid);
        if (page <= 0) throw new ArgumentException(CommonErrorCodes.ApiPageNumberInvalid);

        var orderedSource = orderBy is null ? source : orderBy(source);
        var rows = orderedSource.ToList();

        var rowCount = rows.Count;
        var pageCount = rowCount == 0 ? 0 : (rowCount + size - 1) / size;
        var returnedPage = pageCount > 0 && page > pageCount ? pageCount : page;

        var pageOffset = (returnedPage - 1) * size;
        var pageRows = rows
            .Skip(pageOffset)
            .Take(size)
            .ToList();
        var data = dataFactory(pageRows);

        var firstRowOnPage = rowCount == 0 ? 0 : pageOffset + 1;
        var lastRowOnPage = rowCount == 0 ? 0 : Math.Min(pageOffset + pageRows.Count, rowCount);
        var hasNextPage = pageCount > 0 && returnedPage < pageCount;

        return new PagedResponseModel<TData>
        {
            Page = returnedPage,
            Size = size,
            RowCount = rowCount,
            PageCount = pageCount,
            HasNextPage = hasNextPage,
            FirstRowOnPage = firstRowOnPage,
            LastRowOnPage = lastRowOnPage,
            Data = data,
        };
    }
}
