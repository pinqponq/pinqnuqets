namespace Pinqponq.Contracts;

/// <summary>
/// Builds a <see cref="PagedResponseModel{T}"/> from an in-memory sequence or a query.
/// </summary>
public static class PaginationExtensions
{
    /// <summary>
    /// Orders <paramref name="source"/> (optionally), takes the requested page and wraps it.
    /// </summary>
    /// <exception cref="ArgumentException">See <see cref="PageWindow.ValidateRequest"/>.</exception>
    public static PagedResponseModel<TData> ToPagedResponse<TEntity, TData>(
        this IEnumerable<TEntity> source,
        int page,
        int size,
        Func<List<TEntity>, TData> dataFactory,
        Func<IEnumerable<TEntity>, IEnumerable<TEntity>>? orderBy = null,
        PaginationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dataFactory);
        PageWindow.ValidateRequest(page, size);

        var orderedSource = orderBy is null ? source : orderBy(source);
        var rows = orderedSource.ToList();
        var window = PageWindow.Resolve(page, size, rows.Count, options);
        var pageRows = rows
            .Skip(window.Skip)
            .Take(window.Size)
            .ToList();

        return window.ToResponse(pageRows, dataFactory);
    }

    /// <summary>
    /// Counts <paramref name="query"/> and runs it for the requested page only, so the provider
    /// (EF Core, the MongoDB LINQ provider, …) never loads the other rows. Order the query before
    /// calling it.
    /// </summary>
    /// <exception cref="ArgumentException">See <see cref="PageWindow.ValidateRequest"/>.</exception>
    public static PagedResponseModel<TData> ToPagedResponse<TEntity, TData>(
        this IQueryable<TEntity> query,
        int page,
        int size,
        Func<List<TEntity>, TData> dataFactory,
        PaginationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(dataFactory);
        PageWindow.ValidateRequest(page, size);

        var window = PageWindow.Resolve(page, size, query.LongCount(), options);
        var pageRows = query
            .Skip(window.Skip)
            .Take(window.Size)
            .ToList();

        return window.ToResponse(pageRows, dataFactory);
    }
}
