namespace Pinqponq.Sms;

/// <summary>
/// NetGSM SMS provider configuration. When <see cref="ApiUrl"/> is empty and
/// <see cref="Transport"/> is <see cref="SmsTransport.GetQuery"/>, sending is a
/// no-op when <see cref="AllowNoOp"/> is true. For <see cref="SmsTransport.RestV2"/>,
/// an empty <see cref="ApiUrl"/> uses the default HTTPS REST endpoint.
/// </summary>
public sealed class SmsOptions
{
    /// <summary>Default NetGSM REST v2 send URL.</summary>
    public const string DefaultRestV2ApiUrl = "https://api.netgsm.com.tr/sms/rest/v2/send";

    /// <summary>
    /// SMS API URL. For <see cref="SmsTransport.GetQuery"/>, e.g.
    /// <c>https://api.netgsm.com.tr/sms/send/get/</c>. Empty disables sending when
    /// <see cref="AllowNoOp"/> is <see langword="true"/>. For
    /// <see cref="SmsTransport.RestV2"/>, empty resolves to <see cref="DefaultRestV2ApiUrl"/>.
    /// Must use HTTPS when set.
    /// </summary>
    public string? ApiUrl { get; set; }

    /// <summary>
    /// Transport used to call NetGSM. Defaults to <see cref="SmsTransport.GetQuery"/>.
    /// </summary>
    public SmsTransport Transport { get; set; } = SmsTransport.GetQuery;

    /// <summary>API user code (usercode).</summary>
    public string? UserCode { get; set; }

    /// <summary>API password.</summary>
    public string? Password { get; set; }

    /// <summary>Sender header (msgheader) shown to the recipient.</summary>
    public string? MsgHeader { get; set; }

    /// <summary>Maximum retry attempts on transient HTTP failures. Defaults to 3.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Base delay for exponential backoff between retries. Defaults to 300ms.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// HTTP timeout for NetGSM calls. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/>, an empty <see cref="ApiUrl"/> is allowed for
    /// <see cref="SmsTransport.GetQuery"/> and sending becomes a no-op (local development
    /// only). Defaults to <see langword="false"/>. Ignored for RestV2 (empty URL uses the
    /// default HTTPS endpoint). GetQuery sends credentials on the query string — never log
    /// request URLs.
    /// </summary>
    public bool AllowNoOp { get; set; }
}
