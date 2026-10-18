using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Http;

namespace Pinqponq.Push.Fcm.Tests;

/// <summary>
/// Answers the FCM HTTP v1 send endpoint and the Instance ID topic endpoints the way Google does,
/// so the tests exercise FirebaseAdmin's own response parsing.
/// </summary>
internal sealed class FakeFcmServer : HttpMessageHandler
{
    private readonly Dictionary<string, FakeFcmError> _sendErrorsByToken = new();
    private readonly Dictionary<string, string> _topicErrorsByToken = new();
    private readonly List<JsonNode> _sentMessages = new();
    private readonly List<FakeTopicRequest> _topicRequests = new();
    private readonly object _recordLock = new();

    public IReadOnlyList<JsonNode> SentMessages
    {
        get
        {
            lock (_recordLock)
            {
                return _sentMessages.ToList();
            }
        }
    }

    public IReadOnlyList<FakeTopicRequest> TopicRequests => _topicRequests;

    public void FailSend(string token, FakeFcmError error) => _sendErrorsByToken[token] = error;

    public void FailTopic(string token, string reason) => _topicErrorsByToken[token] = reason;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        var requestJson = JsonNode.Parse(requestBody)!;
        var requestPath = request.RequestUri!.AbsolutePath;

        if (requestPath.EndsWith("/messages:send", StringComparison.Ordinal))
        {
            return AnswerSend(requestJson["message"]!);
        }

        return AnswerTopicChange(requestPath, requestJson);
    }

    private HttpResponseMessage AnswerSend(JsonNode message)
    {
        lock (_recordLock)
        {
            _sentMessages.Add(message);
        }

        var token = message["token"]?.GetValue<string>();
        if (token is not null && _sendErrorsByToken.TryGetValue(token, out var error))
        {
            return JsonResponse(error.HttpStatus, error.ToResponseJson());
        }

        return JsonResponse(HttpStatusCode.OK, """{"name":"projects/test-project/messages/1"}""");
    }

    private HttpResponseMessage AnswerTopicChange(string requestPath, JsonNode requestJson)
    {
        var tokens = requestJson["registration_tokens"]!.AsArray().Select(token => token!.GetValue<string>()).ToList();
        _topicRequests.Add(new FakeTopicRequest(requestPath, requestJson["to"]!.GetValue<string>(), tokens));

        var results = new JsonArray();
        foreach (var token in tokens)
        {
            var result = new JsonObject();
            if (_topicErrorsByToken.TryGetValue(token, out var reason))
            {
                result["error"] = reason;
            }

            results.Add(result);
        }

        return JsonResponse(HttpStatusCode.OK, new JsonObject { ["results"] = results }.ToJsonString());
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed record FakeTopicRequest(string Path, string Topic, IReadOnlyList<string> Tokens);

internal sealed record FakeFcmError(HttpStatusCode HttpStatus, string Status, string? FcmErrorCode)
{
    public static readonly FakeFcmError Unregistered = new(HttpStatusCode.NotFound, "NOT_FOUND", "UNREGISTERED");
    public static readonly FakeFcmError ThirdPartyAuth = new(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "THIRD_PARTY_AUTH_ERROR");
    public static readonly FakeFcmError InvalidArgument = new(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "INVALID_ARGUMENT");
    public static readonly FakeFcmError SenderIdMismatch = new(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "SENDER_ID_MISMATCH");
    public static readonly FakeFcmError PermissionDenied = new(HttpStatusCode.Forbidden, "PERMISSION_DENIED", null);

    public string ToResponseJson()
    {
        var error = new JsonObject
        {
            ["code"] = (int)HttpStatus,
            ["message"] = $"{Status} from the fake FCM server",
            ["status"] = Status,
        };
        if (FcmErrorCode is not null)
        {
            error["details"] = new JsonArray(new JsonObject
            {
                ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError",
                ["errorCode"] = FcmErrorCode,
            });
        }

        return JsonSerializer.Serialize(new JsonObject { ["error"] = error });
    }
}

internal sealed class FakeFcmHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
}
