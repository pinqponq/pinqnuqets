namespace Pinqponq.LiveKit.Server.Models;

/// <summary>
/// Mirrors <c>livekit.Room</c>. Timestamps are Unix time as sent by LiveKit.
/// </summary>
public sealed class Room
{
    public string Sid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public uint EmptyTimeout { get; init; }
    public uint DepartureTimeout { get; init; }
    public uint MaxParticipants { get; init; }
    public long CreationTime { get; init; }
    public long CreationTimeMs { get; init; }
    public string TurnPassword { get; init; } = string.Empty;
    public string Metadata { get; init; } = string.Empty;

    /// <summary>
    /// Excludes hidden participants. Use <c>ListParticipants</c> when the exact participant list is required.
    /// </summary>
    public uint NumParticipants { get; init; }

    public uint NumPublishers { get; init; }
    public bool ActiveRecording { get; init; }
}
