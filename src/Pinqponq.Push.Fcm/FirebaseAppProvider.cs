using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Options;

namespace Pinqponq.Push.Fcm;

/// <summary>
/// Creates the package's own named <see cref="FirebaseApp"/> on first use and reuses it afterwards,
/// so an application that also creates the default Firebase app keeps it untouched.
/// </summary>
internal sealed class FirebaseAppProvider
{
    internal const string APP_NAME = "Pinqponq.Push.Fcm";

    private static readonly object AppCreationLock = new();

    private readonly Func<AppOptions> _createAppOptions;
    private readonly string _appName;

    public FirebaseAppProvider(IOptions<FcmOptions> fcmOptions)
        : this(() => CreateAppOptions(fcmOptions.Value), APP_NAME)
    {
    }

    internal FirebaseAppProvider(Func<AppOptions> createAppOptions, string appName)
    {
        _createAppOptions = createAppOptions;
        _appName = appName;
    }

    public FirebaseMessaging GetMessaging() => FirebaseMessaging.GetMessaging(GetOrCreateApp());

    internal FirebaseApp GetOrCreateApp()
    {
        lock (AppCreationLock)
        {
            return FirebaseApp.GetInstance(_appName) ?? FirebaseApp.Create(_createAppOptions(), _appName);
        }
    }

    private static AppOptions CreateAppOptions(FcmOptions fcmOptions) =>
        new() { Credential = FcmCredentialLoader.Load(fcmOptions) };
}
