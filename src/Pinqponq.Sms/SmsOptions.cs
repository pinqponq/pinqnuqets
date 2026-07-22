namespace Pinqponq.Sms;

/// <summary>
/// NetGSM SMS provider configuration. When <see cref="ApiUrl"/> is empty, sending is a
/// no-op (useful in development).
/// </summary>
public sealed class SmsOptions
{
    /// <summary>
    /// SMS API base URL, e.g. <c>https://api.netgsm.com.tr/sms/send/get/</c>.
    /// Empty disables sending.
    /// </summary>
    public string? ApiUrl { get; set; }

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
}
