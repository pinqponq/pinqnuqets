using Microsoft.AspNetCore.Http;

namespace Pinqponq.Observability.ClientLogs;

/// <summary>Why a client log request was refused.</summary>
public enum ClientLogRejectionReason
{
    /// <summary>The body is not a valid entry or batch: unreadable JSON, no entries, an empty entry or an entry without an event name.</summary>
    InvalidRequest,

    /// <summary>The batch holds more than <see cref="ClientLogOptions.MaxEntriesPerBatch"/> entries.</summary>
    TooManyEntries,

    /// <summary>An entry holds more than <see cref="ClientLogOptions.MaxFieldsPerEntry"/> metadata and detail fields.</summary>
    TooManyFields,

    /// <summary>An event name is longer than <see cref="ClientLogOptions.MaxValueLength"/>.</summary>
    EventTooLong,

    /// <summary>The body is longer than <see cref="ClientLogOptions.MaxBodyBytes"/>.</summary>
    BodyTooLarge
}

/// <summary>
/// Thrown when a client log request is refused. Nothing of the request has been written. It is an
/// <see cref="ArgumentException"/>, so Pinqponq.ErrorHandling answers it with 400 unless the product maps it
/// (for example to <see cref="StatusCode"/> and its own response code) with a status mapping resolver.
/// </summary>
public sealed class ClientLogRejectedException : ArgumentException
{
    public ClientLogRejectedException(ClientLogRejectionReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>Why the request was refused.</summary>
    public ClientLogRejectionReason Reason { get; }

    /// <summary>413 for <see cref="ClientLogRejectionReason.BodyTooLarge"/>, 400 for every other reason.</summary>
    public int StatusCode => Reason is ClientLogRejectionReason.BodyTooLarge
        ? StatusCodes.Status413PayloadTooLarge
        : StatusCodes.Status400BadRequest;
}
