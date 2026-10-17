namespace Pinqponq.Contracts;

/// <summary>
/// Per-service paging limits. <see cref="Default"/> limits the page size to 100. When the requested
/// page is after the last page, <see cref="Default"/> returns the last page.
/// </summary>
public sealed record PaginationOptions
{
    public const int DEFAULT_MAX_PAGE_SIZE = 100;

    /// <summary>The options used when none are passed.</summary>
    public static PaginationOptions Default { get; } = new();

    private readonly int _maxPageSize = DEFAULT_MAX_PAGE_SIZE;

    /// <summary>A larger requested page size is lowered to this value. Must be positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxPageSize
    {
        get => _maxPageSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxPageSize = value;
        }
    }

    /// <summary>What a request for the page after the last page returns.</summary>
    public PastLastPageBehavior PastLastPage { get; init; } = PastLastPageBehavior.ReturnLastPage;
}
