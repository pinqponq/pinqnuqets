namespace Pinqponq.LiveKit.Server;

public sealed class LiveKitCredentials
{
    public string ApiKey { get; }
    public string ApiSecret { get; }

    public LiveKitCredentials(string apiKey, string apiSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiSecret);

        ApiKey = apiKey;
        ApiSecret = apiSecret;
    }

    // The secret must never end up in logs through string interpolation.
    public override string ToString() => $"LiveKitCredentials {{ ApiKey = {ApiKey} }}";
}
