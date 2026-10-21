namespace Pinqponq.Contracts;

/// <summary>
/// Response codes every Pinqponq API shares. Product-specific codes stay in the product.
/// </summary>
public static class CommonErrorCodes
{
    /// <summary>Fallback for an error the API has no specific code for.</summary>
    public const string GeneralError = "general_error";

    /// <summary>The client closed the connection before the response was written.</summary>
    public const string ApiClientClosedRequest = "api_client_closed_request";

    /// <summary>The request has no body although the endpoint needs one.</summary>
    public const string ApiBodyRequired = "api_body_required";

    /// <summary>The requested page size is not valid.</summary>
    public const string ApiPageSizeInvalid = "api_page_size_invalid";

    /// <summary>The requested page number is not valid.</summary>
    public const string ApiPageNumberInvalid = "api_page_number_invalid";

    /// <summary>The caller sent more requests than its rate limit allows.</summary>
    public const string TooManyRequests = "too_many_requests";
}
