namespace Pinqponq.LiveKit.Server.Models;

// Numeric values match livekit_models.proto. Unrecognized is not part of the protocol: it is what a
// value unknown to this SDK version deserializes to, so a newer server does not break older clients.

public enum TrackSource
{
    Unrecognized = -1,
    Unknown = 0,
    Camera = 1,
    Microphone = 2,
    ScreenShare = 3,
    ScreenShareAudio = 4
}

public enum TrackType
{
    Unrecognized = -1,
    Audio = 0,
    Video = 1,
    Data = 2
}

public enum ParticipantState
{
    Unrecognized = -1,
    Joining = 0,
    Joined = 1,
    Active = 2,
    Disconnected = 3
}

public enum ParticipantKind
{
    Unrecognized = -1,
    Standard = 0,
    Ingress = 1,
    Egress = 2,
    Sip = 3,
    Agent = 4,
    Connector = 7,
    Bridge = 8
}

public enum DisconnectReason
{
    Unrecognized = -1,
    UnknownReason = 0,
    ClientInitiated = 1,
    DuplicateIdentity = 2,
    ServerShutdown = 3,
    ParticipantRemoved = 4,
    RoomDeleted = 5,
    StateMismatch = 6,
    JoinFailure = 7,
    Migration = 8,
    SignalClose = 9,
    RoomClosed = 10,
    UserUnavailable = 11,
    UserRejected = 12,
    SipTrunkFailure = 13,
    ConnectionTimeout = 14,
    MediaFailure = 15,
    AgentError = 16
}
