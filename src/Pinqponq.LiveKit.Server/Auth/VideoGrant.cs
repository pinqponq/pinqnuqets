using Pinqponq.LiveKit.Server.Models;
using System.Text.Json.Serialization;

namespace Pinqponq.LiveKit.Server.Auth;

/// <summary>
/// Mirrors the <c>video</c> claim of a LiveKit access token.
/// </summary>
public sealed class VideoGrant
{
    public bool RoomCreate { get; init; }
    public bool RoomList { get; init; }
    public bool RoomRecord { get; init; }
    public bool RoomAdmin { get; init; }
    public bool RoomJoin { get; init; }

    /// <summary>
    /// Required when <see cref="RoomJoin"/> or <see cref="RoomAdmin"/> is set.
    /// </summary>
    public string? Room { get; init; }

    // Nullable on purpose: LiveKit treats an unset publish/subscribe permission as granted, while an explicit false denies it.
    public bool? CanPublish { get; init; }
    public bool? CanSubscribe { get; init; }
    public bool? CanPublishData { get; init; }

    /// <summary>
    /// When set, only these sources can be published. Leave null for no source restriction.
    /// </summary>
    [JsonConverter(typeof(TrackSourceGrantListConverter))]
    public IReadOnlyList<TrackSource>? CanPublishSources { get; init; }

    public bool? CanUpdateOwnMetadata { get; init; }
    public bool IngressAdmin { get; init; }
    public bool Hidden { get; init; }
    public bool Recorder { get; init; }
    public bool Agent { get; init; }
    public bool? CanSubscribeMetrics { get; init; }
    public bool? CanManageAgentSession { get; init; }
    public string? DestinationRoom { get; init; }
}
