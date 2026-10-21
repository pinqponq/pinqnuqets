using Microsoft.AspNetCore.Http;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Writes the standard <see cref="ErrorResponse"/> for a failure that is answered without an
/// exception, for example a request a rate limiter rejected. Registered by
/// <c>AddPinqponqErrorHandling</c>.
/// </summary>
public interface IErrorResponseWriter
{
    /// <summary>
    /// Describes the failure on <c>Pinqponq.Contracts.IRequestErrorFeature</c> under
    /// <paramref name="eventName"/> and writes the error body with <paramref name="statusCode"/>.
    /// With error localization on, the message comes from the catalog in the request's language, and a
    /// code the catalog does not know gets the <c>FallbackResponseCode</c> message. With localization
    /// off, <paramref name="defaultMessage"/> is written.
    /// </summary>
    Task Write(HttpContext context, int statusCode, string responseCode, string defaultMessage, string eventName);
}
