using FirebaseAdmin.Messaging;

namespace Pinqponq.Push.Fcm;

/// <summary>Settings for <see cref="IFcmSender"/>.</summary>
public sealed class FcmOptions
{
    /// <summary>Configuration section the options are conventionally bound from.</summary>
    public const string SectionName = "Fcm";

    /// <summary>
    /// When <see langword="false"/>, nothing is sent and no credential is needed, for example on a
    /// developer machine. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Path to the Firebase service account JSON file. A relative path is resolved against
    /// <see cref="AppContext.BaseDirectory"/>. Set this or <see cref="CredentialJson"/>, not both.
    /// </summary>
    public string? CredentialPath { get; set; }

    /// <summary>
    /// The Firebase service account JSON itself, for example read from Vault. Set this or
    /// <see cref="CredentialPath"/>, not both.
    /// </summary>
    public string? CredentialJson { get; set; }

    /// <summary>
    /// Android priority of the pushes sent by <see cref="IFcmSender.SendDataOnlyToTokens"/>. Defaults to
    /// <see cref="Priority.Normal"/>, the FCM default for data messages. <see cref="Priority.High"/> wakes
    /// the device right away, but Android lowers the priority of an app whose high-priority pushes do not
    /// show a notification.
    /// </summary>
    public Priority DataOnlyAndroidPriority { get; set; } = Priority.Normal;
}
