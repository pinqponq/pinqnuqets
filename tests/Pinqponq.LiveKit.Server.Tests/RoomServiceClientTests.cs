using FluentAssertions;
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

        (handler.LastRequest?.Method).Should().Be(HttpMethod.Post);
        (handler.LastRequest?.RequestUri?.ToString()).Should().Be("http://10.0.0.1:7880/twirp/livekit.RoomService/ListRooms");
        handler.LastRequestBody.Should().Be("""{"names":["room-1"]}""");

        var video = ReadGrant(handler);
        video.GetProperty("roomList").GetBoolean().Should().BeTrue();
        video.TryGetProperty("roomAdmin", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"rooms":[{"sid":"RM_1","name":"room-1","num_participants":2,"creation_time":"1758441600","active_recording":false}]}""")]
    [InlineData("""{"rooms":[{"sid":"RM_1","name":"room-1","numParticipants":2,"creationTime":"1758441600"}]}""")]
    public async Task GetRoom_ParsesSnakeCaseAndCamelCaseResponses(string responseBody)
    {
        var (client, _) = CreateClient(responseBody);

        var room = await client.GetRoom("room-1");

        room.Should().NotBeNull();
        room!.Sid.Should().Be("RM_1");
        room.NumParticipants.Should().Be(2u);
        room.CreationTime.Should().Be(1758441600);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"rooms":[]}""")]
    [InlineData("""{"rooms":[{"name":"room-10"}]}""")]
    public async Task GetRoom_ReturnsNullWhenRoomIsNotOpen(string responseBody)
    {
        var (client, _) = CreateClient(responseBody);

        var room = await client.GetRoom("room-1");

        room.Should().BeNull();
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

        var participant = participants.Should().ContainSingle().Subject;
        participant.Identity.Should().Be("user-1");
        participant.State.Should().Be(ParticipantState.Active);
        participant.IsPublisher.Should().BeTrue();
        participant.Tracks.Should().ContainSingle().Which.Source.Should().Be(TrackSource.Microphone);

        var video = ReadGrant(handler);
        video.GetProperty("roomAdmin").GetBoolean().Should().BeTrue();
        video.GetProperty("room").GetString().Should().Be("room-1");
        handler.LastRequestBody.Should().Be("""{"room":"room-1"}""");
    }

    [Fact]
    public async Task RemoveParticipant_NotFound_ThrowsTypedException()
    {
        var (client, handler) = CreateClient("""{"code":"not_found","msg":"participant does not exist"}""", HttpStatusCode.NotFound);

        var act = () => client.RemoveParticipant("room-1", "user-9");

        var exception = (await act.Should().ThrowExactlyAsync<LiveKitApiException>()).Which;
        exception.IsNotFound.Should().BeTrue();
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Message.Should().Contain("participant does not exist");
        handler.LastRequestBody.Should().Be("""{"room":"room-1","identity":"user-9"}""");
    }

    [Fact]
    public async Task RemoveParticipant_SendsRevocationTimestampWhenGiven()
    {
        var (client, handler) = CreateClient("{}");
        var revokeTokensIssuedBefore = DateTimeOffset.FromUnixTimeSeconds(1758441600);

        await client.RemoveParticipant("room-1", "user-9", revokeTokensIssuedBefore);

        handler.LastRequestBody.Should().Be("""{"room":"room-1","identity":"user-9","revokeTokenTs":1758441600}""");
    }

    [Fact]
    public async Task DeleteRoom_SendsTwirpRequestWithRoomCreateGrant()
    {
        var (client, handler) = CreateClient("{}");

        await client.DeleteRoom("room-1");

        (handler.LastRequest?.RequestUri?.ToString()).Should().Be("http://10.0.0.1:7880/twirp/livekit.RoomService/DeleteRoom");
        handler.LastRequestBody.Should().Be("""{"room":"room-1"}""");

        var video = ReadGrant(handler);
        video.GetProperty("roomCreate").GetBoolean().Should().BeTrue();
        video.TryGetProperty("roomAdmin", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteRoom_NotFound_ThrowsTypedException()
    {
        var (client, _) = CreateClient("""{"code":"not_found","msg":"room not found"}""", HttpStatusCode.NotFound);

        var act = () => client.DeleteRoom("room-1");

        var exception = (await act.Should().ThrowExactlyAsync<LiveKitApiException>()).Which;
        exception.IsNotFound.Should().BeTrue();
    }

    [Fact]
    public async Task NonTwirpErrorBody_HasNoErrorCode()
    {
        var (client, _) = CreateClient("<html>Bad Gateway</html>", HttpStatusCode.BadGateway);

        var act = () => client.ListRooms();

        var exception = (await act.Should().ThrowExactlyAsync<LiveKitApiException>()).Which;
        exception.ErrorCode.Should().BeNull();
        exception.IsNotFound.Should().BeFalse();
        exception.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Theory]
    [InlineData("wss://rtc.example.com", "https://rtc.example.com/twirp/livekit.RoomService/ListRooms")]
    [InlineData("https://rtc.example.com/livekit/", "https://rtc.example.com/livekit/twirp/livekit.RoomService/ListRooms")]
    [InlineData("http://localhost:7880", "http://localhost:7880/twirp/livekit.RoomService/ListRooms")]
    public async Task ServerUrl_IsNormalizedToHttpBaseUrl(string serverUrl, string expectedRequestUrl)
    {
        var (client, handler) = CreateClient("{}", serverUrl: serverUrl);

        await client.ListRooms();

        (handler.LastRequest?.RequestUri?.ToString()).Should().Be(expectedRequestUrl);
    }

    [Fact]
    public void InvalidServerUrl_FailsAtConstruction()
    {
        var serverOptions = new LiveKitServerOptions { Url = "10.0.0.1:7880" };

        var act = () => new RoomServiceClient(new HttpClient(), serverOptions, TestCredentials.Provider);

        act.Should().ThrowExactly<ArgumentException>();
    }

    private static JsonElement ReadGrant(StubHttpMessageHandler handler)
    {
        var authorization = handler.LastRequest?.Headers.Authorization;
        (authorization?.Scheme).Should().Be("Bearer");
        return JwtPayload.Decode(authorization!.Parameter!).GetProperty("video"); // Asserted non-null by the Bearer check above.
    }
}
