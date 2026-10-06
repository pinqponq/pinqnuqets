using Pinqponq.LiveKit.Server.Models;
using Pinqponq.LiveKit.Server.Rooms;
using Pinqponq.LiveKit.Server.Twirp;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Pinqponq.LiveKit.Server.Tests;

public sealed class RoomServiceClientTests
{
    private static (RoomServiceClient Client, StubHttpMessageHandler Handler) CreateClient(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string serverUrl = "ws://10.0.0.1:7880")
    {
        var handler = new StubHttpMessageHandler(statusCode, responseBody);
        var serverOptions = new LiveKitServerOptions { Url = serverUrl };
        var client = new RoomServiceClient(new HttpClient(handler), serverOptions, TestCredentials.Provider);
        return (client, handler);
    }

    [Fact]
    public async Task GetRoom_SendsTwirpRequestWithRoomListGrant()
    {
        var (client, handler) = CreateClient("{}");

        await client.GetRoom("room-1");

        Assert.Equal(HttpMethod.Post, (handler.LastRequest?.Method));
        Assert.Equal("http://10.0.0.1:7880/twirp/livekit.RoomService/ListRooms", (handler.LastRequest?.RequestUri?.ToString()));
        Assert.Equal("""{"names":["room-1"]}""", handler.LastRequestBody);

        var video = ReadGrant(handler);
        Assert.True(video.GetProperty("roomList").GetBoolean());
        Assert.False(video.TryGetProperty("roomAdmin", out _));
    }

    [Theory]
    [InlineData("""{"rooms":[{"sid":"RM_1","name":"room-1","num_participants":2,"creation_time":"1758441600","active_recording":false}]}""")]
    [InlineData("""{"rooms":[{"sid":"RM_1","name":"room-1","numParticipants":2,"creationTime":"1758441600"}]}""")]
    public async Task GetRoom_ParsesSnakeCaseAndCamelCaseResponses(string responseBody)
    {
        var (client, _) = CreateClient(responseBody);

        var room = await client.GetRoom("room-1");

        Assert.NotNull(room);
        Assert.Equal("RM_1", room!.Sid);
        Assert.Equal(2u, room.NumParticipants);
        Assert.Equal(1758441600, room.CreationTime);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"rooms":[]}""")]
    [InlineData("""{"rooms":[{"name":"room-10"}]}""")]
    public async Task GetRoom_ReturnsNullWhenRoomIsNotOpen(string responseBody)
    {
        var (client, _) = CreateClient(responseBody);

        var room = await client.GetRoom("room-1");

        Assert.Null(room);
    }

    [Fact]
    public async Task ListParticipants_UsesRoomAdminGrantAndParsesParticipants()
    {
        const string responseBody = """
            {"participants":[{"sid":"PA_1","identity":"user-1","state":"ACTIVE","joined_at":"1758441601",
              "tracks":[{"sid":"TR_1","type":"AUDIO","source":"MICROPHONE","muted":false}],"is_publisher":true}]}
            """;
        var (client, handler) = CreateClient(responseBody);

        var participants = await client.ListParticipants("room-1");

        var participant = Assert.Single(participants);
        Assert.Equal("user-1", participant.Identity);
        Assert.Equal(ParticipantState.Active, participant.State);
        Assert.True(participant.IsPublisher);
        Assert.Equal(TrackSource.Microphone, Assert.Single(participant.Tracks).Source);

        var video = ReadGrant(handler);
        Assert.True(video.GetProperty("roomAdmin").GetBoolean());
        Assert.Equal("room-1", video.GetProperty("room").GetString());
        Assert.Equal("""{"room":"room-1"}""", handler.LastRequestBody);
    }

    [Fact]
    public async Task RemoveParticipant_NotFound_ThrowsTypedException()
    {
        var (client, handler) = CreateClient("""{"code":"not_found","msg":"participant does not exist"}""", HttpStatusCode.NotFound);

        var act = () => client.RemoveParticipant("room-1", "user-9");

        var exception = await Assert.ThrowsAsync<LiveKitApiException>(act);
        Assert.True(exception.IsNotFound);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("participant does not exist", exception.Message);
        Assert.Equal("""{"room":"room-1","identity":"user-9"}""", handler.LastRequestBody);
    }

    [Fact]
    public async Task RemoveParticipant_SendsRevocationTimestampWhenGiven()
    {
        var (client, handler) = CreateClient("{}");
        var revokeTokensIssuedBefore = DateTimeOffset.FromUnixTimeSeconds(1758441600);

        await client.RemoveParticipant("room-1", "user-9", revokeTokensIssuedBefore);

        Assert.Equal("""{"room":"room-1","identity":"user-9","revokeTokenTs":1758441600}""", handler.LastRequestBody);
    }

    [Fact]
    public async Task DeleteRoom_SendsTwirpRequestWithRoomCreateGrant()
    {
        var (client, handler) = CreateClient("{}");

        await client.DeleteRoom("room-1");

        Assert.Equal("http://10.0.0.1:7880/twirp/livekit.RoomService/DeleteRoom", (handler.LastRequest?.RequestUri?.ToString()));
        Assert.Equal("""{"room":"room-1"}""", handler.LastRequestBody);

        var video = ReadGrant(handler);
        Assert.True(video.GetProperty("roomCreate").GetBoolean());
        Assert.False(video.TryGetProperty("roomAdmin", out _));
    }

    [Fact]
    public async Task DeleteRoom_NotFound_ThrowsTypedException()
    {
        var (client, _) = CreateClient("""{"code":"not_found","msg":"room not found"}""", HttpStatusCode.NotFound);

        var act = () => client.DeleteRoom("room-1");

        var exception = await Assert.ThrowsAsync<LiveKitApiException>(act);
        Assert.True(exception.IsNotFound);
    }

    [Fact]
    public async Task NonTwirpErrorBody_HasNoErrorCode()
    {
        var (client, _) = CreateClient("<html>Bad Gateway</html>", HttpStatusCode.BadGateway);

        var act = () => client.ListRooms();

        var exception = await Assert.ThrowsAsync<LiveKitApiException>(act);
        Assert.Null(exception.ErrorCode);
        Assert.False(exception.IsNotFound);
        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Theory]
    [InlineData("wss://rtc.example.com", "https://rtc.example.com/twirp/livekit.RoomService/ListRooms")]
    [InlineData("https://rtc.example.com/livekit/", "https://rtc.example.com/livekit/twirp/livekit.RoomService/ListRooms")]
    [InlineData("http://localhost:7880", "http://localhost:7880/twirp/livekit.RoomService/ListRooms")]
    public async Task ServerUrl_IsNormalizedToHttpBaseUrl(string serverUrl, string expectedRequestUrl)
    {
        var (client, handler) = CreateClient("{}", serverUrl: serverUrl);

        await client.ListRooms();

        Assert.Equal(expectedRequestUrl, (handler.LastRequest?.RequestUri?.ToString()));
    }

    [Fact]
    public void InvalidServerUrl_FailsAtConstruction()
    {
        var serverOptions = new LiveKitServerOptions { Url = "10.0.0.1:7880" };

        var act = () => new RoomServiceClient(new HttpClient(), serverOptions, TestCredentials.Provider);

        Assert.Throws<ArgumentException>(act);
    }

    private static JsonElement ReadGrant(StubHttpMessageHandler handler)
    {
        var authorization = handler.LastRequest?.Headers.Authorization;
        Assert.Equal("Bearer", (authorization?.Scheme));
        return JwtPayload.Decode(authorization!.Parameter!).GetProperty("video"); // Asserted non-null by the Bearer check above.
    }
}
