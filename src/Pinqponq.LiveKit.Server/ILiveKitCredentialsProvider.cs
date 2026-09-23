namespace Pinqponq.LiveKit.Server;

/// <summary>
/// Supplies the API key/secret pair used to sign tokens and verify webhooks.
/// Implement it to read credentials from a secret store; caching is the implementation's responsibility.
/// </summary>
public interface ILiveKitCredentialsProvider
{
    ValueTask<LiveKitCredentials> GetCredentials(CancellationToken cancellationToken);
}
