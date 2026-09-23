namespace Pinqponq.LiveKit.Server.Twirp;

/// <summary>
/// Error codes defined by the Twirp protocol, as returned in the <c>code</c> field of an error response.
/// </summary>
public static class TwirpErrorCode
{
    public const string Canceled = "canceled";
    public const string Unknown = "unknown";
    public const string InvalidArgument = "invalid_argument";
    public const string Malformed = "malformed";
    public const string DeadlineExceeded = "deadline_exceeded";
    public const string NotFound = "not_found";
    public const string BadRoute = "bad_route";
    public const string AlreadyExists = "already_exists";
    public const string PermissionDenied = "permission_denied";
    public const string Unauthenticated = "unauthenticated";
    public const string ResourceExhausted = "resource_exhausted";
    public const string FailedPrecondition = "failed_precondition";
    public const string Aborted = "aborted";
    public const string OutOfRange = "out_of_range";
    public const string Unimplemented = "unimplemented";
    public const string Internal = "internal";
    public const string Unavailable = "unavailable";
    public const string DataLoss = "data_loss";
}
