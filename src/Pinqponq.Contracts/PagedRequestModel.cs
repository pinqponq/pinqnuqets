using Microsoft.AspNetCore.Mvc;

namespace Pinqponq.Contracts;

/// <summary>
/// Paging query parameters, bound from <c>?PageNumber=1&amp;PageSize=10</c>.
/// </summary>
public class PagedRequestModel
{
    /// <summary>One-based page number. Defaults to 1.</summary>
    [FromQuery(Name = "PageNumber")]
    public int Page { get; set; } = 1;

    /// <summary>Page size. Defaults to 10.</summary>
    [FromQuery(Name = "PageSize")]
    public int Size { get; set; } = 10;
}
