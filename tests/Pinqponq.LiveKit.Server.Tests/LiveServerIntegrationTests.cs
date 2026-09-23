using FluentAssertions;
using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.Rooms;
using Pinqponq.LiveKit.Server.Twirp;
using Xunit;
using Xunit.Abstractions;

namespace Pinqponq.LiveKit.Server.Tests;

/// <summary>
/// Runs against a real LiveKit server when LIVEKIT_TEST_URL, LIVEKIT_TEST_API_KEY and LIVEKIT_TEST_API_SECRET are set;
/// otherwise every test returns immediately.
/// </summary>
public sealed class LiveServerIntegrationTests
{
    private const string ROOM_SERVICE_NAME = "livekit.RoomService";

    private readonly ITestOutputHelper _output;
    private readonly LiveKitServerOptions? _serverOptions;
    private readonly StaticLiveKitCredentialsProvider? _credentialsProvider;

    public LiveServerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        var url = Environment.GetEnvironmentVariable("LIVEKIT_TEST_URL");
        var apiKey = Environment.GetEnvironmentVariable("LIVEKIT_TEST_API_KEY");
        var apiSecret = Environment.GetEnvironmentVariable("LIVEKIT_TEST_API_SECRET");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            return;
        }

        _serverOptions = new LiveKitServerOptions { Url = url };
        _credentialsProvider = new StaticLiveKitCredentialsProvider(new LiveKitCredentials(apiKey, apiSecret));
    }

    private bool IsConfigured => _serverOptions is not null && _credentialsProvider is not null;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RoomLifecycle_AgainstLiveServer()
    {
        if (!IsConfigured)
        {
            _output.WriteLine("LIVEKIT_TEST_* not set; skipped.");
            return;
        }

        var httpClient = new HttpClient();
        var roomServiceClient = new RoomServiceClient(httpClient, _serverOptions!, _credentialsProvider!); // Non-null when IsConfigured.
        var twirpClient = new TwirpClient(httpClient, _serverOptions!, _credentialsProvider!, TimeProvider.System);
        var roomName = $"sdk-integration-{Guid.NewGuid():N}";

        var missingRoom = await roomServiceClient.GetRoom(roomName);
        missingRoom.Should().BeNull();

        await CreateRoom(twirpClient, roomName);
        try
        {
            var createdRoom = await roomServiceClient.GetRoom(roomName);
            createdRoom.Should().NotBeNull();
            createdRoom!.NumParticipants.Should().Be(0u);
            createdRoom.Sid.Should().NotBeEmpty();
            createdRoom.CreationTime.Should().BePositive();
            _output.WriteLine($"GetRoom: sid={createdRoom.Sid} participants={createdRoom.NumParticipants} created={createdRoom.CreationTime}");

            var participants = await roomServiceClient.ListParticipants(roomName);
            participants.Should().BeEmpty();

            var removeAbsentParticipant = () => roomServiceClient.RemoveParticipant(roomName, "nobody");
            var removeException = (await removeAbsentParticipant.Should().ThrowExactlyAsync<LiveKitApiException>()).Which;
            _output.WriteLine($"RemoveParticipant(absent): code={removeException.ErrorCode} status={(int)removeException.StatusCode} message={removeException.Message}");
            removeException.IsNotFound.Should().BeTrue();
        }
        finally
        {
            await roomServiceClient.DeleteRoom(roomName);
        }

        var deletedRoom = await roomServiceClient.GetRoom(roomName);
        deletedRoom.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteRoom_OnMissingRoom_AgainstLiveServer()
    {
        if (!IsConfigured)
        {
            _output.WriteLine("LIVEKIT_TEST_* not set; skipped.");
            return;
        }

        var roomServiceClient = new RoomServiceClient(new HttpClient(), _serverOptions!, _credentialsProvider!); // Non-null when IsConfigured.
        var roomName = $"sdk-integration-missing-{Guid.NewGuid():N}";

        try
        {
            await roomServiceClient.DeleteRoom(roomName);
            _output.WriteLine("DeleteRoom(missing room) succeeded.");
        }
        catch (LiveKitApiException exception)
        {
            _output.WriteLine($"DeleteRoom(missing room) threw code={exception.ErrorCode} status={(int)exception.StatusCode} notFound={exception.IsNotFound}.");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListParticipants_OnMissingRoom_AgainstLiveServer()
    {
        if (!IsConfigured)
        {
            _output.WriteLine("LIVEKIT_TEST_* not set; skipped.");
            return;
        }

        var roomServiceClient = new RoomServiceClient(new HttpClient(), _serverOptions!, _credentialsProvider!); // Non-null when IsConfigured.
        var roomName = $"sdk-integration-missing-{Guid.NewGuid():N}";

        try
        {
            var participants = await roomServiceClient.ListParticipants(roomName);
            _output.WriteLine($"ListParticipants(missing room) returned {participants.Count} participants.");
        }
        catch (LiveKitApiException exception)
        {
            _output.WriteLine($"ListParticipants(missing room) threw code={exception.ErrorCode} status={(int)exception.StatusCode}.");
        }
    }

    // CreateRoom is not part of the SDK surface yet; it is called through the internal Twirp client for setup only.
    private static Task CreateRoom(TwirpClient twirpClient, string roomName) =>
        twirpClient.Call<object, object>(
            ROOM_SERVICE_NAME, "CreateRoom", new { name = roomName, emptyTimeout = 60 }, new VideoGrant { RoomCreate = true }, CancellationToken.None);
}
