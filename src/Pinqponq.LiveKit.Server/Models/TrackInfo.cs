namespace Pinqponq.LiveKit.Server.Models;

/// <summary>
/// Mirrors <c>livekit.TrackInfo</c>.
/// </summary>
public sealed class TrackInfo
{
    public string Sid { get; init; } = string.Empty;
    public TrackType Type { get; init; }
    public TrackSource Source { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public bool Muted { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public string Mid { get; init; } = string.Empty;
    public string Stream { get; init; } = string.Empty;
}
