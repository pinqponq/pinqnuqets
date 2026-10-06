namespace Pinqponq.LiveKit.Server.Webhooks;

/// <summary>
/// Known values of <see cref="WebhookEvent.Event"/>. The raw name is always available on the event itself.
/// </summary>
public enum WebhookEventType
{
    Unrecognized,
    RoomStarted,
    RoomFinished,
    ParticipantJoined,
    ParticipantLeft,
    ParticipantConnectionAborted,
    TrackPublished,
    TrackUnpublished,
    EgressStarted,
    EgressUpdated,
    EgressEnded,
    IngressStarted,
    IngressEnded,
    AgentJobStarted,
    AgentJobEnded
}
