using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.Models;
using Pinqponq.LiveKit.Server.Twirp;

namespace Pinqponq.LiveKit.Server.Rooms;

/// <summary>
/// Client for <c>livekit.RoomService</c>. Failed calls throw <see cref="LiveKitApiException"/>.
/// </summary>
public sealed class RoomServiceClient
{
    private const string SERVICE_NAME = "livekit.RoomService";

    private readonly TwirpClient _twirpClient;

    public RoomServiceClient(
        HttpClient httpClient,
        LiveKitServerOptions serverOptions,
        ILiveKitCredentialsProvider credentialsProvider,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serverOptions);
        ArgumentNullException.ThrowIfNull(credentialsProvider);

        _twirpClient = new TwirpClient(httpClient, serverOptions, credentialsProvider, timeProvider ?? TimeProvider.System);
    }

    /// <summary>
    /// Lists active rooms, optionally only those with the given names. Requires <c>roomList</c>.
    /// </summary>
    public async Task<IReadOnlyList<Room>> ListRooms(IEnumerable<string>? roomNames = null, CancellationToken cancellationToken = default)
    {
        var request = new ListRoomsRequest { Names = roomNames?.ToList() };
        var grant = new VideoGrant { RoomList = true };

        var response = await _twirpClient.Call<ListRoomsRequest, ListRoomsResponse>(
            SERVICE_NAME, "ListRooms", request, grant, cancellationToken);
        return response.Rooms ?? [];
    }

    /// <summary>
    /// Returns the active room with this name, or null when no such room is open. Requires <c>roomList</c>.
    /// </summary>
    public async Task<Room?> GetRoom(string roomName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);

        var rooms = await ListRooms([roomName], cancellationToken);
        return rooms.FirstOrDefault(room => room.Name == roomName);
    }

    /// <summary>
    /// Lists every participant in the room, hidden ones included. Requires <c>roomAdmin</c>.
    /// </summary>
    public async Task<IReadOnlyList<ParticipantInfo>> ListParticipants(string roomName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);

        var request = new ListParticipantsRequest { Room = roomName };
        var grant = CreateRoomAdminGrant(roomName);

        var response = await _twirpClient.Call<ListParticipantsRequest, ListParticipantsResponse>(
            SERVICE_NAME, "ListParticipants", request, grant, cancellationToken);
        return response.Participants ?? [];
    }

    /// <summary>
    /// Disconnects a participant from the room. Requires <c>roomAdmin</c>.
    /// Throws <see cref="LiveKitApiException"/> with <see cref="LiveKitApiException.IsNotFound"/> when the participant is not in the room.
    /// </summary>
    /// <param name="roomName">The room the participant is connected to.</param>
    /// <param name="identity">The participant's identity.</param>
    /// <param name="revokeTokensIssuedBefore">
    /// LiveKit Cloud only: tokens whose <c>nbf</c> is earlier than this instant are rejected on reconnect.
    /// Must be within 60 seconds of the server time. When null the server uses its own default.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task RemoveParticipant(
        string roomName,
        string identity,
        DateTimeOffset? revokeTokensIssuedBefore = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        var request = new RoomParticipantIdentity
        {
            Room = roomName,
            Identity = identity,
            RevokeTokenTs = revokeTokensIssuedBefore?.ToUnixTimeSeconds()
        };
        var grant = CreateRoomAdminGrant(roomName);

        await _twirpClient.Call<RoomParticipantIdentity, RemoveParticipantResponse>(
            SERVICE_NAME, "RemoveParticipant", request, grant, cancellationToken);
    }

    /// <summary>
    /// Deletes the room and disconnects every participant in it. Requires <c>roomCreate</c>.
    /// </summary>
    /// <param name="roomName">The room to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task DeleteRoom(string roomName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);

        var request = new DeleteRoomRequest { Room = roomName };
        var grant = new VideoGrant { RoomCreate = true };

        await _twirpClient.Call<DeleteRoomRequest, DeleteRoomResponse>(
            SERVICE_NAME, "DeleteRoom", request, grant, cancellationToken);
    }

    private static VideoGrant CreateRoomAdminGrant(string roomName) => new() { RoomAdmin = true, Room = roomName };

    private sealed class ListRoomsRequest
    {
        public List<string>? Names { get; init; }
    }

    private sealed class ListRoomsResponse
    {
        public IReadOnlyList<Room>? Rooms { get; init; }
    }

    private sealed class ListParticipantsRequest
    {
        public required string Room { get; init; }
    }

    private sealed class ListParticipantsResponse
    {
        public IReadOnlyList<ParticipantInfo>? Participants { get; init; }
    }

    private sealed class RoomParticipantIdentity
    {
        public required string Room { get; init; }
        public required string Identity { get; init; }
        public long? RevokeTokenTs { get; init; }
    }

    private sealed class RemoveParticipantResponse
    {
    }

    private sealed class DeleteRoomRequest
    {
        public required string Room { get; init; }
    }

    private sealed class DeleteRoomResponse
    {
    }
}
