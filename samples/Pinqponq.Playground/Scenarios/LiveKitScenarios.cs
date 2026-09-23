using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Pinqponq.LiveKit.Server;
using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.DependencyInjection;
using Pinqponq.LiveKit.Server.Models;
using Pinqponq.LiveKit.Server.Rooms;
using Pinqponq.LiveKit.Server.Webhooks;
using Pinqponq.Playground.Scenarios.Support;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for <c>Pinqponq.LiveKit.Server</c>. Tokens and webhooks are pure signing and
/// verification, so they run offline; RoomService needs a LiveKit server you supply.
/// </summary>
public static class LiveKitScenarios
{
    private const string Package = "Pinqponq.LiveKit.Server";

    // Offline scenarios never contact a server; the URL only has to pass options validation.
    private const string OfflineUrl = "ws://localhost:7880";

    private const string DefaultApiKey = "playground-key";
    private const string DefaultApiSecret = "playground-secret-long-enough-for-hs256";

    private static readonly ScenarioField ApiKeyField =
        new("apiKey", "API key", ScenarioFieldKind.Text, DefaultApiKey);

    private static readonly ScenarioField ApiSecretField =
        new("apiSecret", "API secret", ScenarioFieldKind.Password, DefaultApiSecret);

    public static IEnumerable<Scenario> Create()
    {
        yield return IssueToken();
        yield return GrantChecks();
        yield return ReceiveWebhook();
        yield return RejectForgedWebhooks();
        yield return InvalidUrl();
        yield return RealServerRooms();
    }

    private static Scenario IssueToken() => new(
        new ScenarioDescriptor
        {
            Id = "livekit.token.issue",
            PackageId = Package,
            Title = "Issue a room access token",
            Summary = "Signs the token a client joins a room with, then decodes it to show the claims "
                      + "LiveKit reads: sub (identity), iss (API key), exp, and the video grant.",
            Fields =
            [
                new ScenarioField("identity", "Identity", ScenarioFieldKind.Text, "user-123"),
                new ScenarioField("name", "Display name", ScenarioFieldKind.Text, "Jane Doe"),
                new ScenarioField("room", "Room", ScenarioFieldKind.Text, "playground-room"),
                new ScenarioField("canPublish", "CanPublish", ScenarioFieldKind.Bool, "true",
                    "Unchecked writes an explicit false, which denies publishing."),
                new ScenarioField("ttlMinutes", "Ttl (minutes)", ScenarioFieldKind.Number, "360"),
                ApiKeyField,
                ApiSecretField,
            ],
        },
        async context =>
        {
            var apiKey = context.Input.Text("apiKey");
            await using var host = context.Host(services => services
                .AddPinqponqLiveKitServer(options => options.Url = OfflineUrl)
                .AddStaticCredentials(apiKey, context.Input.Text("apiSecret")));

            var identity = context.Input.Text("identity");
            var room = context.Input.Text("room");
            var token = await host.GetRequiredService<AccessTokenIssuer>().CreateToken(
                new AccessTokenOptions
                {
                    Identity = identity,
                    Name = context.Input.TextOrNull("name"),
                    Ttl = TimeSpan.FromMinutes(context.Input.Int("ttlMinutes")),
                    VideoGrant = new VideoGrant
                    {
                        RoomJoin = true,
                        Room = room,
                        CanPublish = context.Input.Bool("canPublish"),
                    },
                },
                context.CancellationToken);

            context.Step("Token signed", $"expires at {token.ExpiresAt:u}");
            context.Artifact("token", token.Value, "token");
            context.Artifact("decoded", Presentation.Jwt(token.Value));

            var payload = DecodePayload(token.Value);
            context.Require("sub is the identity", (string?)payload["sub"] == identity);
            context.Require("iss is the API key", (string?)payload["iss"] == apiKey);
            context.Require("video.room is the room", (string?)payload["video"]?["room"] == room);
            context.Require("video.roomJoin is true", (bool?)payload["video"]?["roomJoin"] == true);
            context.Check(
                "exp matches ExpiresAt",
                (long?)payload["exp"] == token.ExpiresAt.ToUnixTimeSeconds(),
                $"{payload["exp"]}");
        });

    private static Scenario GrantChecks() => new(
        new ScenarioDescriptor
        {
            Id = "livekit.token.grant-checks",
            PackageId = Package,
            Title = "Invalid token options are refused",
            Summary = "A RoomJoin grant without an identity or a room, and a non-positive TTL, are "
                      + "refused before anything is signed — LiveKit would reject such a token at join time.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services => services
                .AddPinqponqLiveKitServer(options => options.Url = OfflineUrl)
                .AddStaticCredentials(DefaultApiKey, DefaultApiSecret));

            var issuer = host.GetRequiredService<AccessTokenIssuer>();

            var withoutIdentity = await CaptureAsync(() => issuer.CreateToken(
                new AccessTokenOptions { VideoGrant = new VideoGrant { RoomJoin = true, Room = "playground-room" } },
                context.CancellationToken));
            context.Require(
                "RoomJoin without Identity → ArgumentException",
                withoutIdentity?.GetType() == typeof(ArgumentException),
                withoutIdentity?.Message);

            var withoutRoom = await CaptureAsync(() => issuer.CreateToken(
                new AccessTokenOptions { Identity = "user-123", VideoGrant = new VideoGrant { RoomJoin = true } },
                context.CancellationToken));
            context.Require(
                "RoomJoin without Room → ArgumentException",
                withoutRoom?.GetType() == typeof(ArgumentException),
                withoutRoom?.Message);

            var zeroTtl = await CaptureAsync(() => issuer.CreateToken(
                new AccessTokenOptions { Identity = "user-123", Ttl = TimeSpan.Zero },
                context.CancellationToken));
            context.Require(
                "Ttl of zero → ArgumentOutOfRangeException",
                zeroTtl is ArgumentOutOfRangeException,
                zeroTtl?.Message);

            context.Artifact("refusals", new[]
            {
                Describe("RoomJoin without Identity", withoutIdentity),
                Describe("RoomJoin without Room", withoutRoom),
                Describe("Ttl = 0", zeroTtl),
            }, "table");
        });

    private static Scenario ReceiveWebhook() => new(
        new ScenarioDescriptor
        {
            Id = "livekit.webhook.receive",
            PackageId = Package,
            Title = "Verify a webhook and read the event",
            Summary = "Signs a body the way the LiveKit server does — an HS256 token carrying the body's "
                      + "SHA-256 — then WebhookReceiver verifies it and parses LiveKit's protojson payload.",
            Fields =
            [
                new ScenarioField("event", "Event", ScenarioFieldKind.Enum, "participant_joined", null,
                    ["participant_joined", "participant_left", "room_started", "room_finished"]),
                new ScenarioField("room", "Room", ScenarioFieldKind.Text, "playground-room"),
                new ScenarioField("identity", "Participant identity", ScenarioFieldKind.Text, "user-123"),
                ApiKeyField,
                ApiSecretField,
            ],
        },
        async context =>
        {
            var apiKey = context.Input.Text("apiKey");
            var apiSecret = context.Input.Text("apiSecret");
            var eventName = context.Input.Text("event");
            var room = context.Input.Text("room");
            var identity = context.Input.Text("identity");
            var now = DateTimeOffset.UtcNow;

            var body = BuildWebhookBody(eventName, room, identity, now);
            var authorizationHeader = SignWebhook(body, apiKey, apiSecret, now);
            context.Step("Webhook signed like the LiveKit server", "sha256 claim = Base64(SHA-256(body))");
            context.Artifact("request body", JsonNode.Parse(body));
            context.Artifact("Authorization header", authorizationHeader, "token");

            await using var host = context.Host(services => services
                .AddPinqponqLiveKitServer(options => options.Url = OfflineUrl)
                .AddStaticCredentials(apiKey, apiSecret));

            var webhookEvent = await host.GetRequiredService<WebhookReceiver>()
                .Receive(body, authorizationHeader, context.CancellationToken);
            context.Step("Signature, issuer, lifetime and body checksum verified");

            context.Require("Event name parsed", webhookEvent.Event == eventName, webhookEvent.Event);
            context.Require(
                "EventType recognized",
                webhookEvent.EventType != WebhookEventType.Unrecognized,
                webhookEvent.EventType.ToString());
            context.Require("Room parsed", webhookEvent.Room?.Name == room, webhookEvent.Room?.Name);
            if (IsParticipantEvent(eventName))
            {
                context.Require(
                    "Participant parsed",
                    webhookEvent.Participant?.Identity == identity,
                    webhookEvent.Participant?.Identity);
            }

            context.Artifact("event", new
            {
                id = webhookEvent.Id,
                @event = webhookEvent.Event,
                eventType = webhookEvent.EventType.ToString(),
                createdAt = webhookEvent.CreatedAt,
                room = webhookEvent.Room is null ? null : new { webhookEvent.Room.Sid, webhookEvent.Room.Name },
                participant = webhookEvent.Participant is null
                    ? null
                    : new
                    {
                        webhookEvent.Participant.Identity,
                        state = webhookEvent.Participant.State.ToString(),
                    },
            });
        });

    private static Scenario RejectForgedWebhooks() => new(
        new ScenarioDescriptor
        {
            Id = "livekit.webhook.forged",
            PackageId = Package,
            Title = "Forged and altered webhooks are rejected",
            Summary = "A body changed after signing, a token signed with another secret, an expired "
                      + "token, and a missing Authorization header each raise LiveKitWebhookValidationException.",
            NegativePath = true,
            Fields = [ApiKeyField, ApiSecretField],
        },
        async context =>
        {
            var apiKey = context.Input.Text("apiKey");
            var apiSecret = context.Input.Text("apiSecret");
            var now = DateTimeOffset.UtcNow;

            await using var host = context.Host(services => services
                .AddPinqponqLiveKitServer(options => options.Url = OfflineUrl)
                .AddStaticCredentials(apiKey, apiSecret));

            var receiver = host.GetRequiredService<WebhookReceiver>();
            var body = BuildWebhookBody("participant_joined", "playground-room", "user-123", now);
            var authorizationHeader = SignWebhook(body, apiKey, apiSecret, now);

            var genuine = await CaptureAsync(() => receiver.Receive(body, authorizationHeader, context.CancellationToken));
            context.Require("The untouched webhook is accepted", genuine is null, genuine?.Message);

            var alteredBody = body.Replace("user-123", "intruder", StringComparison.Ordinal);
            var altered = await CaptureAsync(() =>
                receiver.Receive(alteredBody, authorizationHeader, context.CancellationToken));
            context.Require("Body altered after signing", altered is LiveKitWebhookValidationException, altered?.Message);

            var forgedHeader = SignWebhook(body, apiKey, "not-the-real-api-secret-for-this-key", now);
            var forged = await CaptureAsync(() => receiver.Receive(body, forgedHeader, context.CancellationToken));
            context.Require("Signed with another secret", forged is LiveKitWebhookValidationException, forged?.Message);

            var expiredHeader = SignWebhook(body, apiKey, apiSecret, now.AddMinutes(-10));
            var expired = await CaptureAsync(() => receiver.Receive(body, expiredHeader, context.CancellationToken));
            context.Require("Expired token", expired is LiveKitWebhookValidationException, expired?.Message);

            var missing = await CaptureAsync(() => receiver.Receive(body, null, context.CancellationToken));
            context.Require("No Authorization header", missing is LiveKitWebhookValidationException, missing?.Message);

            context.Artifact("rejections", new[]
            {
                Describe("Body altered after signing", altered),
                Describe("Signed with another secret", forged),
                Describe("Expired token", expired),
                Describe("No Authorization header", missing),
            }, "table");
        });

    private static Scenario InvalidUrl() => new(
        new ScenarioDescriptor
        {
            Id = "livekit.options.invalid-url",
            PackageId = Package,
            Title = "An invalid server URL fails validation",
            Summary = "LiveKitServerOptions.Url must be an absolute http, https, ws or wss URL. Anything "
                      + "else fails options validation — on startup in a real host, via ValidateOnStart.",
            NegativePath = true,
            Fields = [new ScenarioField("url", "Url", ScenarioFieldKind.Text, "not a url")],
        },
        async context =>
        {
            await using var host = context.Host(services => services
                .AddPinqponqLiveKitServer(options => options.Url = context.Input.Text("url"))
                .AddStaticCredentials(DefaultApiKey, DefaultApiSecret));

            var thrown = Capture(() => _ = host.GetRequiredService<IOptions<LiveKitServerOptions>>().Value);
            context.Require("OptionsValidationException", thrown is OptionsValidationException, thrown?.Message);
            context.Check(
                "The error names the accepted schemes",
                thrown?.Message.Contains("http, https, ws or wss", StringComparison.Ordinal) == true,
                thrown?.Message);
            context.Artifact("exception", Describe("Url validation", thrown));
        });

    private static Scenario RealServerRooms() => new(
        new ScenarioDescriptor
        {
            Id = "livekit.rooms.real-server",
            PackageId = Package,
            Title = "List rooms on a real LiveKit server",
            Summary = "Enter a LiveKit server you can reach — LiveKit Cloud, or `livekit-server --dev` "
                      + "locally (devkey / secret). Lists the open rooms and, when a room name is given, "
                      + "its participants.",
            Fields =
            [
                new ScenarioField("url", "Server URL", ScenarioFieldKind.Text, null,
                    "e.g. wss://my-project.livekit.cloud or ws://localhost:7880", Required: true),
                new ScenarioField("apiKey", "API key", ScenarioFieldKind.Text, null, Required: true),
                new ScenarioField("apiSecret", "API secret", ScenarioFieldKind.Password, null, Required: true),
                new ScenarioField("room", "Room (optional)", ScenarioFieldKind.Text, null,
                    "Also lists this room's participants."),
            ],
        },
        async context =>
        {
            var url = context.Input.TextOrNull("url");
            var apiKey = context.Input.TextOrNull("apiKey");
            var apiSecret = context.Input.TextOrNull("apiSecret");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            {
                ScenarioContext.Skip("Enter the URL, API key and API secret of a LiveKit server.");
                return;
            }

            await using var host = context.Host(services => services
                .AddPinqponqLiveKitServer(options => options.Url = url)
                .AddStaticCredentials(apiKey, apiSecret));

            var roomServiceClient = host.GetRequiredService<RoomServiceClient>();

            IReadOnlyList<Room> rooms;
            try
            {
                rooms = await roomServiceClient.ListRooms(cancellationToken: context.CancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                ScenarioContext.Skip($"Could not reach the LiveKit server: {exception.Message}");
                return;
            }

            context.Step("ListRooms", $"{rooms.Count} open room(s)");
            context.Artifact("rooms", rooms.Select(openRoom => new
            {
                name = openRoom.Name,
                sid = openRoom.Sid,
                participants = openRoom.NumParticipants,
                publishers = openRoom.NumPublishers,
                createdAt = DateTimeOffset.FromUnixTimeSeconds(openRoom.CreationTime),
            }).ToArray(), "table");

            var roomName = context.Input.TextOrNull("room");
            if (roomName is null)
            {
                return;
            }

            if (await roomServiceClient.GetRoom(roomName, context.CancellationToken) is null)
            {
                context.Step("GetRoom", $"'{roomName}' is not open — GetRoom returned null");
                return;
            }

            var participants = await roomServiceClient.ListParticipants(roomName, context.CancellationToken);
            context.Step("ListParticipants", $"{participants.Count} participant(s) in '{roomName}'");
            context.Artifact("participants", participants.Select(participant => new
            {
                identity = participant.Identity,
                name = participant.Name,
                state = participant.State.ToString(),
                kind = participant.Kind.ToString(),
            }).ToArray(), "table");
        });

    private static bool IsParticipantEvent(string eventName) =>
        eventName.StartsWith("participant_", StringComparison.Ordinal);

    /// <summary>
    /// A webhook body in the shape LiveKit's protojson produces: camelCase keys, int64 values
    /// as strings, and a participant only on participant_* events.
    /// </summary>
    private static string BuildWebhookBody(string eventName, string room, string identity, DateTimeOffset now)
    {
        var unixSeconds = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var body = new JsonObject
        {
            ["event"] = eventName,
            ["room"] = new JsonObject
            {
                ["sid"] = "RM_playground",
                ["name"] = room,
                ["numParticipants"] = 1,
                ["creationTime"] = unixSeconds,
            },
            ["id"] = $"EV_{Guid.NewGuid():N}",
            ["createdAt"] = unixSeconds,
        };

        if (IsParticipantEvent(eventName))
        {
            body["participant"] = new JsonObject
            {
                ["sid"] = "PA_playground",
                ["identity"] = identity,
                ["state"] = "ACTIVE",
                ["joinedAt"] = unixSeconds,
            };
        }

        return body.ToJsonString();
    }

    /// <summary>
    /// Produces the Authorization header the LiveKit server sends with a webhook: an HS256
    /// token issued by the API key, valid for five minutes from <paramref name="issuedAt"/>,
    /// whose sha256 claim is the Base64 SHA-256 of the exact body.
    /// </summary>
    private static string SignWebhook(string body, string apiKey, string apiSecret, DateTimeOffset issuedAt)
    {
        var checksum = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var claims = new JsonObject
        {
            ["iss"] = apiKey,
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddMinutes(5).ToUnixTimeSeconds(),
            ["sha256"] = checksum,
        };

        var signingInput = $"{EncodeSegment("""{"alg":"HS256","typ":"JWT"}""")}.{EncodeSegment(claims.ToJsonString())}";
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiSecret), Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{ToBase64Url(signature)}";
    }

    private static string EncodeSegment(string json) => ToBase64Url(Encoding.UTF8.GetBytes(json));

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonNode DecodePayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        var padded = segment.PadRight(segment.Length + ((4 - (segment.Length % 4)) % 4), '=');
        return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded)))
               ?? throw new ScenarioAssertionException("The token payload is empty.");
    }

    private static object Describe(string attempt, Exception? exception) => new
    {
        attempt,
        exception = exception?.GetType().Name,
        message = exception?.Message,
    };

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
