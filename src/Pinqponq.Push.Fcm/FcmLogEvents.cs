namespace Pinqponq.Push.Fcm;

/// <summary>Event names of the log records <see cref="IFcmSender"/> writes through <c>ILogService</c>.</summary>
public static class FcmLogEvents
{
    public const string SendPartialFailure = "fcm_send_partial_failure";
    public const string ProviderCredentialInvalid = "fcm_provider_credential_invalid";
    public const string TopicSubscribePartialFailure = "fcm_topic_subscribe_partial_failure";
    public const string TopicUnsubscribePartialFailure = "fcm_topic_unsubscribe_partial_failure";
    public const string Disabled = "fcm_disabled";
}
