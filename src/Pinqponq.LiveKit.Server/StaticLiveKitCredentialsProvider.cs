namespace Pinqponq.LiveKit.Server;

public sealed class StaticLiveKitCredentialsProvider : ILiveKitCredentialsProvider
{
    private readonly LiveKitCredentials _credentials;

    public StaticLiveKitCredentialsProvider(LiveKitCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        _credentials = credentials;
    }

    public ValueTask<LiveKitCredentials> GetCredentials(CancellationToken cancellationToken) => ValueTask.FromResult(_credentials);
}
