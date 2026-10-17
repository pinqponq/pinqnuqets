namespace Pinqponq.ErrorHandling;

/// <summary>
/// How an <see cref="IExceptionResolver"/> answers an exception.
/// </summary>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="ResponseCode">Response code. With error localization enabled it is looked up in the catalog.</param>
public sealed record ExceptionResolution(int StatusCode, string ResponseCode)
{
    /// <summary>
    /// Code used when localization is enabled and the catalog does not know <see cref="ResponseCode"/>.
    /// <see langword="null"/> falls back to <see cref="ErrorLocalizationOptions.FallbackResponseCode"/>.
    /// </summary>
    public string? FallbackResponseCode { get; init; }

    /// <summary>
    /// Exception whose type names the log event (<c>exception_&lt;TypeName&gt;</c>), e.g. the
    /// driver exception wrapped by an ORM exception. <see langword="null"/> uses the caught exception.
    /// </summary>
    public Exception? ReportedException { get; init; }

    /// <summary>Extra fields appended to the request error detail.</summary>
    public IReadOnlyDictionary<string, object?>? LogDetail { get; init; }
}
