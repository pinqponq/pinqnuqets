using FluentAssertions;
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

        webhookEvent.EventType.Should().Be(WebhookEventType.ParticipantJoined);
        webhookEvent.Id.Should().Be("EV_123");
        webhookEvent.CreatedAt.Should().Be(1758441602);
        (webhookEvent.Room?.Name).Should().Be("project-1-call-c1");
        (webhookEvent.Room?.CreationTime).Should().Be(1758441600);
        (webhookEvent.Room?.NumParticipants).Should().Be(2u);

        var participant = webhookEvent.Participant;
        participant.Should().NotBeNull();
        participant!.Identity.Should().Be("user-1");
        participant.State.Should().Be(ParticipantState.Active);
        participant.Kind.Should().Be(ParticipantKind.Agent);
        participant.Attributes["userName"].Should().Be("kept-as-is");
        (participant.Permission?.CanPublishSources).Should().Equal(TrackSource.Microphone, TrackSource.Camera);
        participant.DisconnectReason.Should().Be(DisconnectReason.Unrecognized);
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

        webhookEvent.EventType.Should().Be(WebhookEventType.ParticipantJoined);
    }

    [Fact]
    public void ReferenceSdk_AcceptsWebhookSignedByThisSdk()
    {
        // The reference SDK throws on enum values it does not know, so this body only uses known ones.
        const string body = """{"event":"participant_joined","participant":{"identity":"user-1","state":"ACTIVE"},"id":"EV_1","createdAt":"1"}""";
        var authorizationHeader = WebhookSigner.Sign(body, TestCredentials.Credentials, DateTimeOffset.UtcNow);
        var referenceReceiver = new Reference.WebhookReceiver(TestCredentials.API_KEY, TestCredentials.API_SECRET);

        var referenceEvent = referenceReceiver.Receive(body, authorizationHeader);

        referenceEvent.Event.Should().Be("participant_joined");
    }

    [Fact]
    public async Task Receive_AcceptsBearerPrefixedHeader()
    {
        var token = WebhookSigner.Sign(PARTICIPANT_JOINED_BODY, TestCredentials.Credentials, Now);

        var webhookEvent = await _receiver.Receive(PARTICIPANT_JOINED_BODY, $"Bearer {token}");

        webhookEvent.Id.Should().Be("EV_123");
    }

    [Fact]
    public async Task Receive_RejectsTamperedBody()
    {
        var authorizationHeader = WebhookSigner.Sign(PARTICIPANT_JOINED_BODY, TestCredentials.Credentials, Now);
        var tamperedBody = PARTICIPANT_JOINED_BODY.Replace("user-1", "intruder");

        var act = () => _receiver.Receive(tamperedBody, authorizationHeader);

        await act.Should().ThrowExactlyAsync<LiveKitWebhookValidationException>();
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

        await receiveWithForeignHeader.Should().ThrowExactlyAsync<LiveKitWebhookValidationException>();
        await receiveWithExpiredHeader.Should().ThrowExactlyAsync<LiveKitWebhookValidationException>();
        await receiveWithoutHeader.Should().ThrowExactlyAsync<LiveKitWebhookValidationException>();
    }

    [Fact]
    public async Task Receive_UnknownEventName_IsUnrecognizedButKeepsRawName()
    {
        const string body = """{"event":"agent_session_paused","id":"EV_9","createdAt":"1"}""";
        var authorizationHeader = WebhookSigner.Sign(body, TestCredentials.Credentials, Now);

        var webhookEvent = await _receiver.Receive(body, authorizationHeader);

        webhookEvent.EventType.Should().Be(WebhookEventType.Unrecognized);
        webhookEvent.Event.Should().Be("agent_session_paused");
    }
}
