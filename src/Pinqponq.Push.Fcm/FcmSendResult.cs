namespace Pinqponq.Push.Fcm;

/// <summary>What FCM answered for one device token of a send.</summary>
public sealed class FcmSendResult
{
    public required string Token { get; init; }

    /// <summary>
    /// <see langword="true"/> when FCM accepted the push for this token. Acceptance is not proof of
    /// delivery: FCM hands the push to APNs or to the device afterwards.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// The FCM error code, for example <c>Unregistered</c> or <c>ThirdPartyAuthError</c>. When the request
    /// failed below the messaging layer, the general Firebase error code. <see langword="null"/> on success.
    /// </summary>
    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>
    /// <see langword="true"/> when FCM reports that the token will never be delivered to again, so it must
    /// be deleted rather than retried. These tokens are also passed to <see cref="IFcmInvalidTokenHandler"/>.
    /// </summary>
    public bool IsTokenInvalid { get; init; }

    /// <summary>
    /// <see langword="true"/> when FCM could not authenticate against the platform push provider, for
    /// example an invalid or missing APNs key. It is not this token's problem: every push to that platform
    /// fails for the same reason until the credentials are fixed.
    /// </summary>
    public bool IsProviderCredentialInvalid { get; init; }
}
