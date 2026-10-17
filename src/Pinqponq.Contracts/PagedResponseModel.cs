using System.ComponentModel.DataAnnotations;

namespace Pinqponq.Contracts;

/// <summary>
/// A page of <typeparamref name="T"/> inside the standard response envelope.
/// </summary>
/// <typeparam name="T">Page content type, usually a list.</typeparam>
public class PagedResponseModel<T> : BaseResponseModel
{
    /// <summary>One-based page number actually returned.</summary>
    public int Page { get; set; }

    /// <summary>Number of rows per page that this response used (after the maximum page size cap).</summary>
    public int Size { get; set; }

    /// <summary>Total number of rows across all pages.</summary>
    public int RowCount { get; set; }

    /// <summary>Total number of pages; 0 when there are no rows.</summary>
    public int PageCount { get; set; }

    /// <summary>Whether a page follows this one.</summary>
    public bool HasNextPage { get; set; }

    /// <summary>One-based index of the first row on this page; 0 when there are no rows.</summary>
    public int FirstRowOnPage { get; set; }

    /// <summary>One-based index of the last row on this page; 0 when there are no rows.</summary>
    public int LastRowOnPage { get; set; }

    /// <summary>Page content.</summary>
    [Required]
    public required T Data { get; set; }
}
