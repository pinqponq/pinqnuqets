namespace Pinqponq.LiveKit.Server;

public sealed class LiveKitServerOptions
{
    /// <summary>
    /// LiveKit server address used for server API calls. Accepts http(s) and ws(s) schemes and an optional sub path.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
