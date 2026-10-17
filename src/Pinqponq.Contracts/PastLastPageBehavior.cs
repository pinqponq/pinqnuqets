namespace Pinqponq.Contracts;

/// <summary>
/// What a request for the page after the last page returns.
/// </summary>
public enum PastLastPageBehavior
{
    /// <summary>
    /// Returns the last page. The response's <c>page</c> field holds the last page number, not the requested one.
    /// When there are no rows, there is no last page, so the response is an empty page with the requested number.
    /// </summary>
    ReturnLastPage,

    /// <summary>
    /// Returns an empty page. The <c>page</c> field keeps the requested number, <c>hasNextPage</c> is false,
    /// and <c>firstRowOnPage</c> and <c>lastRowOnPage</c> are 0.
    /// </summary>
    ReturnEmptyPage,
}
