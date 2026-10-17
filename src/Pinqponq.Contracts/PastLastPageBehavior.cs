namespace Pinqponq.Contracts;

/// <summary>
/// What a page number past the last page returns.
/// </summary>
public enum PastLastPageBehavior
{
    /// <summary>The last page, with <c>page</c> set to the last page number (rindle-backend's contract).</summary>
    ReturnLastPage,

    /// <summary>An empty page with the requested <c>page</c>, <c>hasNextPage</c> false and row positions 0.</summary>
    ReturnEmptyPage,
}
