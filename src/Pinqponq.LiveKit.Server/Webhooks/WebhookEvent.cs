using Pinqponq.LiveKit.Server.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinqponq.LiveKit.Server.Webhooks;

/// <summary>
/// Mirrors <c>livekit.WebhookEvent</c> for room, participant and track events.
/// Egress, ingress and agent job payloads are not modeled; read them from the raw body if needed.
/// </summary>
public sealed class WebhookEvent
{
    private static readonly Dictionary<string, WebhookEventType> EventTypeByName = Enum.GetValues<WebhookEventType>()
        .Where(eventType => eventType != WebhookEventType.Unrecognized)
        .ToDictionary(eventType => JsonNamingPolicy.SnakeCaseLower.ConvertName(eventType.ToString()), StringComparer.Ordinal);

    /// <summary>
    /// Unique event id. LiveKit retries failed deliveries, so use it to discard duplicates.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Raw event name, e.g. <c>participant_joined</c>.
    /// </summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>
    /// Unix time in seconds.
    /// </summary>
    public long CreatedAt { get; init; }

    public Room? Room { get; init; }

    /// <summary>
    /// Set for participant_* and track_* events.
    /// </summary>
    public ParticipantInfo? Participant { get; init; }

    /// <summary>
    /// Set for track_* events.
    /// </summary>
    public TrackInfo? Track { get; init; }

    [JsonIgnore]
    public WebhookEventType EventType => EventTypeByName.GetValueOrDefault(Event, WebhookEventType.Unrecognized);
}
