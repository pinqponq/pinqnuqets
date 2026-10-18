using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace Pinqponq.Push.Fcm.Tests;

/// <summary>A sender wired to its own Firebase app, which talks to <see cref="FakeFcmServer"/>.</summary>
internal sealed class FcmTestApp : IDisposable
{
    private readonly string _appName = $"fcm-test-{Guid.NewGuid():N}";

    public FcmTestApp(Action<FcmOptions>? configure = null)
    {
        var fcmOptions = new FcmOptions();
        configure?.Invoke(fcmOptions);
        AppProvider = new FirebaseAppProvider(CreateAppOptions, _appName);
        Sender = new FcmSender(AppProvider, InvalidTokenHandler, LogService, Options.Create(fcmOptions));
    }

    public FakeFcmServer Server { get; } = new();

    public RecordingLogService LogService { get; } = new();

    public RecordingInvalidTokenHandler InvalidTokenHandler { get; } = new();

    public FirebaseAppProvider AppProvider { get; }

    public FcmSender Sender { get; }

    public bool IsAppCreated => FirebaseApp.GetInstance(_appName) is not null;

    public void Dispose() => FirebaseApp.GetInstance(_appName)?.Delete();

    private AppOptions CreateAppOptions() =>
        new()
        {
            Credential = GoogleCredential.FromAccessToken("test-access-token"),
            ProjectId = "test-project",
            HttpClientFactory = new FakeFcmHttpClientFactory(Server),
        };
}
