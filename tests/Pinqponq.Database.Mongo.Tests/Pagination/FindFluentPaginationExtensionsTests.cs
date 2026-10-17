using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Pinqponq.Contracts;
using Pinqponq.Database.Mongo.Pagination;
using Xunit;

namespace Pinqponq.Database.Mongo.Tests.Pagination;

public sealed class FindFluentPaginationExtensionsTests
{
    [Fact]
    public async Task Find_is_counted_and_only_the_requested_page_is_read()
    {
        var find = new InMemoryFindFluent(Enumerable.Range(1, 23).ToList());

        var response = await find.ToPagedResponseAsync(page: 2, size: 10, documents => documents);

        Assert.Equal(1, find.CountCallCount);
        Assert.Equal(10, find.AppliedSkip);
        Assert.Equal(10, find.AppliedLimit);
        Assert.Equal(2, response.Page);
        Assert.Equal(23, response.RowCount);
        Assert.Equal(3, response.PageCount);
        Assert.True(response.HasNextPage);
        Assert.Equal(11, response.FirstRowOnPage);
        Assert.Equal(20, response.LastRowOnPage);
        Assert.Equal(Enumerable.Range(11, 10), response.Data);
    }

    [Fact]
    public async Task Page_past_the_end_and_page_size_follow_the_options()
    {
        var find = new InMemoryFindFluent(Enumerable.Range(1, 23).ToList());
        var pinqloqOptions = new PaginationOptions { MaxPageSize = 5, PastLastPage = PastLastPageBehavior.ReturnEmptyPage };

        var response = await find.ToPagedResponseAsync(page: 9, size: 200, documents => documents, pinqloqOptions);

        Assert.Equal(23, find.AppliedSkip);
        Assert.Equal(5, find.AppliedLimit);
        Assert.Equal(9, response.Page);
        Assert.Equal(5, response.Size);
        Assert.False(response.HasNextPage);
        Assert.Empty(response.Data);
    }

    [Fact]
    public async Task Cancellation_reaches_the_count_and_the_read()
    {
        var find = new InMemoryFindFluent(Enumerable.Range(1, 3).ToList());
        using var cancellation = new CancellationTokenSource();

        await find.ToPagedResponseAsync(page: 1, size: 10, documents => documents, cancellationToken: cancellation.Token);

        Assert.Equal([cancellation.Token, cancellation.Token], find.ReceivedCancellationTokens);
    }

    [Theory]
    [InlineData(0, 10, CommonErrorCodes.ApiPageNumberInvalid)]
    [InlineData(1, 0, CommonErrorCodes.ApiPageSizeInvalid)]
    public async Task Invalid_request_is_rejected_before_counting(int page, int size, string expectedResponseCode)
    {
        var find = new InMemoryFindFluent([1, 2, 3]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => find.ToPagedResponseAsync(page, size, documents => documents));

        Assert.Equal(expectedResponseCode, exception.Message);
        Assert.Equal(0, find.CountCallCount);
    }

    [Fact]
    public async Task Missing_find_or_data_factory_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ((IFindFluent<int, int>)null!).ToPagedResponseAsync(1, 10, documents => documents));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new InMemoryFindFluent([1]).ToPagedResponseAsync<int, int, List<int>>(1, 10, null!));
    }

    private sealed class InMemoryFindFluent(List<int> documents) : FindFluentBase<int, int>
    {
        public int CountCallCount { get; private set; }

        public int? AppliedSkip { get; private set; }

        public int? AppliedLimit { get; private set; }

        public List<CancellationToken> ReceivedCancellationTokens { get; } = [];

        public override FilterDefinition<int> Filter { get; set; } = FilterDefinition<int>.Empty;

        public override FindOptions<int, int> Options { get; } = new();

        public override Task<long> CountDocumentsAsync(CancellationToken cancellationToken = default)
        {
            CountCallCount++;
            ReceivedCancellationTokens.Add(cancellationToken);
            return Task.FromResult((long)documents.Count);
        }

        public override IFindFluent<int, int> Skip(int? skip)
        {
            AppliedSkip = skip;
            return this;
        }

        public override IFindFluent<int, int> Limit(int? limit)
        {
            AppliedLimit = limit;
            return this;
        }

        public override Task<IAsyncCursor<int>> ToCursorAsync(CancellationToken cancellationToken = default)
        {
            ReceivedCancellationTokens.Add(cancellationToken);
            var pageDocuments = documents
                .Skip(AppliedSkip ?? 0)
                .Take(AppliedLimit ?? documents.Count)
                .ToList();
            return Task.FromResult<IAsyncCursor<int>>(new SingleBatchCursor(pageDocuments));
        }

        [Obsolete("Abstract in FindFluentBase; the extension uses CountDocumentsAsync.")]
        public override Task<long> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public override IFindFluent<int, TResult> As<TResult>(IBsonSerializer<TResult> resultSerializer) => throw new NotSupportedException();

        public override IFindFluent<int, TNewProjection> Project<TNewProjection>(ProjectionDefinition<int, TNewProjection> projection) => throw new NotSupportedException();

        public override IFindFluent<int, int> Sort(SortDefinition<int> sort) => throw new NotSupportedException();
    }

    private sealed class SingleBatchCursor(List<int> batch) : IAsyncCursor<int>
    {
        private bool _hasMoved;

        public IEnumerable<int> Current => batch;

        public bool MoveNext(CancellationToken cancellationToken = default)
        {
            var hasBatch = !_hasMoved;
            _hasMoved = true;
            return hasBatch;
        }

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(MoveNext(cancellationToken));

        public void Dispose()
        {
        }
    }
}
