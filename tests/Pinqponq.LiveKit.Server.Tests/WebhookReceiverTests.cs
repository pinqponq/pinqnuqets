using Pinqponq.LiveKit.Server.Models;
using Pinqponq.LiveKit.Server.Webhooks;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Reference = Livekit.Server.Sdk.Dotnet;

namespace Pinqponq.LiveKit.Server.Tests;

public sealed class WebhookReceiverTests
{
    // Shape produced by LiveKit's protojson.Marshal: camelCase keys, int64 as strings, enums by name, defaults omitted.
    private const string PARTICIPANT_JOINED_BODY = """
        {
          "event": "participant_joined",
          "room": { "sid": "RM_abc", "name": "project-1-call-c1", "emptyTimeout": 300, "creationTime": "1758441600", "numParticipants": 2 },
          "participant": {
            "sid": "PA_xyz",
            "identity": "user-1",
            "state": "ACTIVE",
            "joinedAt": "1758441601",
            "name": "User One",
            "kind": "AGENT",
            "attributes": { "userName": "kept-as-is" },
            "permission": { "canSubscribe": true, "canPublish": true, "canPublishSources": ["MICROPHONE", "CAMERA"] },
            "disconnectReason": "SOME_FUTURE_REASON"
          },
          "id": "EV_123",
          "createdAt": "1758441602"
        }
        """;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly WebhookReceiver _receiver = new(TestCredentials.Provider, new FixedTimeProvider(Now));

    [Fact]
    public async Task Receive_ParsesProtoJsonPayload()
    {
        var authorizationHeader = WebhookSigner.Sign(PARTICIPANT_JOINED_BODY, TestCredentials.Credentials, Now);

        var webhookEvent = await _receiver.Receive(PARTICIPANT_JOINED_BODY, authorizationHeader);

        Assert.Equal(WebhookEventType.ParticipantJoined, webhookEvent.EventType);
        Assert.Equal("EV_123", webhookEvent.Id);
        Assert.Equal(1758441602, webhookEvent.CreatedAt);
        Assert.Equal("project-1-call-c1", (webhookEvent.Room?.Name));
        Assert.Equal(1758441600, (webhookEvent.Room?.CreationTime));
        Assert.Equal(2u, (webhookEvent.Room?.NumParticipants));

        var participant = webhookEvent.Participant;
        Assert.NotNull(participant);
        Assert.Equal("user-1", participant!.Identity);
        Assert.Equal(ParticipantState.Active, participant.State);
        Assert.Equal(ParticipantKind.Agent, participant.Kind);
        Assert.Equal("kept-as-is", participant.Attributes["userName"]);
        Assert.Equal(new[] { TrackSource.Microphone, TrackSource.Camera }, participant.Permission?.CanPublishSources);
        Assert.Equal(DisconnectReason.Unrecognized, participant.DisconnectReason);
    }

    [Fact]
    public async Task Receive_AcceptsWebhookSignedByReferenceSdk()
    {
        var checksum = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(PARTICIPANT_JOINED_BODY)));
        var referenceToken = new Reference.AccessToken(TestCredentials.API_KEY, TestCredentials.API_SECRET)
            .WithTtl(TimeSpan.FromMinutes(5))
            .WithSha256(checksum)
            .ToJwt();
        var receiver = new WebhookReceiver(TestCredentials.Provider);

        var webhookEvent = await receiver.Receive(PARTICIPANT_JOINED_BODY, referenceToken);

        Assert.Equal(WebhookEventType.ParticipantJoined, webhookEvent.EventType);
    }

    [Fact]
    public void ReferenceSdk_AcceptsWebhookSignedByThisSdk()
    {
        // The reference SDK throws on enum values it does not know, so this body only uses known ones.
        const string body = """{"event":"participant_joined","participant":{"identity":"user-1","state":"ACTIVE"},"id":"EV_1","createdAt":"1"}""";
        var authorizationHeader = WebhookSigner.Sign(body, TestCredentials.Credentials, DateTimeOffset.UtcNow);
        var referenceReceiver = new Reference.WebhookReceiver(TestCredentials.API_KEY, TestCredentials.API_SECRET);

        var referenceEvent = referenceReceiver.Receive(body, authorizationHeader);

        Assert.Equal("participant_joined", referenceEvent.Event);
    }

    [Fact]
    public async Task Receive_AcceptsBearerPrefixedHeader()
    {
        var token = WebhookSigner.Sign(PARTICIPANT_JOINED_BODY, TestCredentials.Credentials, Now);

        var webhookEvent = await _receiver.Receive(PARTICIPANT_JOINED_BODY, $"Bearer {token}");

        Assert.Equal("EV_123", webhookEvent.Id);
    }

    [Fact]
    public async Task Receive_RejectsTamperedBody()
    {
        var authorizationHeader = WebhookSigner.Sign(PARTICIPANT_JOINED_BODY, TestCredentials.Credentials, Now);
        var tamperedBody = PARTICIPANT_JOINED_BODY.Replace("user-1", "intruder");

        var act = () => _receiver.Receive(tamperedBody, authorizationHeader);

        await Assert.ThrowsAsync<LiveKitWebhookValidationException>(act);
    }

    [Fact]
    public async Task Receive_RejectsWrongSecretExpiredAndMissingHeader()
    {
        var foreignCredentials = new LiveKitCredentials(TestCredentials.API_KEY, "another-secret-with-enough-length-000");
        var foreignHeader = WebhookSigner.Sign(PARTICIPANT_JOINED_BODY, foreignCredentials, Now);
        var expiredHeader = WebhookSigner.Sign(PARTICIPANT_JOINED_BODY, TestCredentials.Credentials, Now.AddMinutes(-10));

        var receiveWithForeignHeader = () => _receiver.Receive(PARTICIPANT_JOINED_BODY, foreignHeader);
        var receiveWithExpiredHeader = () => _receiver.Receive(PARTICIPANT_JOINED_BODY, expiredHeader);
        var receiveWithoutHeader = () => _receiver.Receive(PARTICIPANT_JOINED_BODY, null);

        await Assert.ThrowsAsync<LiveKitWebhookValidationException>(receiveWithForeignHeader);
        await Assert.ThrowsAsync<LiveKitWebhookValidationException>(receiveWithExpiredHeader);
        await Assert.ThrowsAsync<LiveKitWebhookValidationException>(receiveWithoutHeader);
    }

    [Fact]
    public async Task Receive_UnknownEventName_IsUnrecognizedButKeepsRawName()
    {
        const string body = """{"event":"agent_session_paused","id":"EV_9","createdAt":"1"}""";
        var authorizationHeader = WebhookSigner.Sign(body, TestCredentials.Credentials, Now);

        var webhookEvent = await _receiver.Receive(body, authorizationHeader);

        Assert.Equal(WebhookEventType.Unrecognized, webhookEvent.EventType);
        Assert.Equal("agent_session_paused", webhookEvent.Event);
    }
}
