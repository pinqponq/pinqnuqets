namespace Pinqponq.Contracts;

/// <summary>
/// Per-service paging limits. <see cref="Default"/> caps the page size at 100 and answers a page
/// number past the last page with the last page.
/// </summary>
public sealed record PaginationOptions
{
    public const int DEFAULT_MAX_PAGE_SIZE = 100;

    /// <summary>The options used when none are passed.</summary>
    public static PaginationOptions Default { get; } = new();

    /// <summary>A larger requested page size is lowered to this value. Must be positive.</summary>
    public int MaxPageSize { get; init; } = DEFAULT_MAX_PAGE_SIZE;

    /// <summary>What a page number past the last page returns.</summary>
    public PastLastPageBehavior PastLastPage { get; init; } = PastLastPageBehavior.ReturnLastPage;
}
