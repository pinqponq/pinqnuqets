namespace Pinqponq.LiveKit.Server.Models;

/// <summary>
/// Mirrors <c>livekit.ParticipantInfo</c>. Timestamps are Unix time as sent by LiveKit.
/// </summary>
public sealed class ParticipantInfo
{
    public string Sid { get; init; } = string.Empty;
    public string Identity { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ParticipantState State { get; init; }
    public IReadOnlyList<TrackInfo> Tracks { get; init; } = [];
    public string Metadata { get; init; } = string.Empty;
    public long JoinedAt { get; init; }
    public long JoinedAtMs { get; init; }
    public uint Version { get; init; }
    public ParticipantPermission? Permission { get; init; }
    public string Region { get; init; } = string.Empty;
    public bool IsPublisher { get; init; }
    public ParticipantKind Kind { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
    public DisconnectReason DisconnectReason { get; init; }
}
