namespace Pinqponq.Sms;

/// <summary>HTTP transport used by <see cref="NetGsmSmsSender"/>.</summary>
public enum SmsTransport
{
    /// <summary>Legacy NetGSM GET with credentials on the query string (default).</summary>
    GetQuery = 0,

    /// <summary>NetGSM REST v2 POST with Basic Auth and a JSON body.</summary>
    RestV2 = 1,
}
