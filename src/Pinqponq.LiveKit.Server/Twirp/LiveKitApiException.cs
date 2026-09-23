using System.Net;

namespace Pinqponq.LiveKit.Server.Twirp;

/// <summary>
/// Thrown when a LiveKit server API call returns a non-success status.
/// </summary>
public sealed class LiveKitApiException : Exception
{
    /// <summary>
    /// Twirp error code (see <see cref="TwirpErrorCode"/>); null when the response was not a Twirp error, e.g. a proxy error page.
    /// </summary>
    public string? ErrorCode { get; }

    public HttpStatusCode StatusCode { get; }

    public bool IsNotFound => ErrorCode == TwirpErrorCode.NotFound;

    public LiveKitApiException(string message, string? errorCode, HttpStatusCode statusCode)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}
