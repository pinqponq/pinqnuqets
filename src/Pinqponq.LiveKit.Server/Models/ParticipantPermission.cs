namespace Pinqponq.LiveKit.Server.Models;

/// <summary>
/// Mirrors <c>livekit.ParticipantPermission</c>.
/// </summary>
public sealed class ParticipantPermission
{
    public bool CanSubscribe { get; init; }
    public bool CanPublish { get; init; }
    public bool CanPublishData { get; init; }
    public IReadOnlyList<TrackSource> CanPublishSources { get; init; } = [];
    public bool Hidden { get; init; }
    public bool CanUpdateMetadata { get; init; }
    public bool CanSubscribeMetrics { get; init; }
    public bool CanManageAgentSession { get; init; }
}
