namespace Pinqponq.Sms;

/// <summary>
/// Thrown when NetGSM accepts the HTTP call but returns a business-error body
/// (e.g. code <c>30</c>). Not retried by <see cref="NetGsmSmsSender"/>.
/// </summary>
public sealed class NetGsmRejectedException : Exception
{
    /// <summary>Creates the exception with the raw provider response body.</summary>
    public NetGsmRejectedException(string responseBody)
        : base($"NetGSM rejected the SMS request: '{responseBody}'.")
    {
        ResponseBody = responseBody;
    }

    /// <summary>Raw response body from NetGSM.</summary>
    public string ResponseBody { get; }
}
