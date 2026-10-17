using MongoDB.Driver;
using Pinqponq.Contracts;

namespace Pinqponq.Database.Mongo.Pagination;

/// <summary>
/// Pages a MongoDB find into a <see cref="PagedResponseModel{T}"/> with one count and one
/// skip/limit query.
/// </summary>
public static class FindFluentPaginationExtensions
{
    /// <summary>
    /// Counts the documents matching <paramref name="find"/> and reads only the requested page.
    /// Sort the find before calling it and do not set its skip or limit. The call sets them on
    /// <paramref name="find"/>, so do not reuse it afterwards.
    /// </summary>
    /// <exception cref="ArgumentException">See <see cref="PageWindow.ValidateRequest"/>.</exception>
    public static async Task<PagedResponseModel<TData>> ToPagedResponseAsync<TDocument, TProjection, TData>(
        this IFindFluent<TDocument, TProjection> find,
        int page,
        int size,
        Func<List<TProjection>, TData> dataFactory,
        PaginationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(dataFactory);
        PageWindow.ValidateRequest(page, size);

        var documentCount = await find.CountDocumentsAsync(cancellationToken).ConfigureAwait(false);
        var window = PageWindow.Resolve(page, size, documentCount, options);
        var pageDocuments = await find
            .Skip(window.Skip)
            .Limit(window.Size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return window.ToResponse(pageDocuments, dataFactory);
    }
}
