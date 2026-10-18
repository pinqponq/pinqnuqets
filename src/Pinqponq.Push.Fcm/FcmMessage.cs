using FirebaseAdmin.Messaging;

namespace Pinqponq.Push.Fcm;

/// <summary>A push with a visible notification, sent to device tokens or to a topic.</summary>
public sealed class FcmMessage
{
    public required string Title { get; init; }

    public required string Body { get; init; }

    /// <summary>Image shown in the notification. Empty or whitespace sends no image.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Key-value pairs delivered to the app together with the notification.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>Android-specific settings. <see langword="null"/> keeps the FCM defaults.</summary>
    public AndroidConfig? Android { get; init; }

    /// <summary>APNs-specific settings (sound, badge, content-available). <see langword="null"/> keeps the FCM defaults.</summary>
    public ApnsConfig? Apns { get; init; }
}
