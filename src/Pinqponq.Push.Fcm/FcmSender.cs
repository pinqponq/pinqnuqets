using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Options;
using Pinqponq.Observability;

namespace Pinqponq.Push.Fcm;

internal sealed class FcmSender : IFcmSender
{
    private const int MAX_TOKENS_PER_MULTICAST = 500;
    private const int MAX_TOKENS_PER_TOPIC_REQUEST = 1000;
    private const int TOKEN_SUFFIX_LENGTH = 6;

    private readonly FirebaseAppProvider _firebaseAppProvider;
    private readonly IFcmInvalidTokenHandler _invalidTokenHandler;
    private readonly ILogService _logService;
    private readonly FcmOptions _fcmOptions;

    public FcmSender(
        FirebaseAppProvider firebaseAppProvider,
        IFcmInvalidTokenHandler invalidTokenHandler,
        ILogService logService,
        IOptions<FcmOptions> fcmOptions)
    {
        _firebaseAppProvider = firebaseAppProvider;
        _invalidTokenHandler = invalidTokenHandler;
        _logService = logService;
        _fcmOptions = fcmOptions.Value;
    }

    public Task<IReadOnlyList<FcmSendResult>> SendToTokens(
        FcmMessage message,
        IReadOnlyCollection<string> tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(tokens);

        return SendInBatches(tokens, tokenBatch => BuildNotificationMulticast(message, tokenBatch), cancellationToken);
    }

    public Task<IReadOnlyList<FcmSendResult>> SendDataOnlyToTokens(
        IReadOnlyDictionary<string, string> data,
        IReadOnlyCollection<string> tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(tokens);
        if (data.Count == 0)
        {
            throw new ArgumentException("A data-only push needs at least one data entry.", nameof(data));
        }

        return SendInBatches(tokens, tokenBatch => BuildDataOnlyMulticast(data, tokenBatch), cancellationToken);
    }

    public async Task SendToTopic(string topic, FcmMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(message);
        if (IsDisabled(tokenCount: 0))
        {
            return;
        }

        var topicMessage = new Message
        {
            Topic = topic,
            Notification = BuildNotification(message),
            Data = CopyData(message.Data),
            Android = message.Android,
            Apns = message.Apns,
        };
        await _firebaseAppProvider.GetMessaging().SendAsync(topicMessage, cancellationToken).ConfigureAwait(false);
    }

    public Task SubscribeToTopic(IReadOnlyCollection<string> tokens, string topic, CancellationToken cancellationToken = default) =>
        ChangeTopicSubscription(
            tokens,
            topic,
            (messaging, tokenBatch) => messaging.SubscribeToTopicAsync(tokenBatch, topic),
            "FCM rejected some tokens while subscribing them to a topic.",
            FcmLogEvents.TopicSubscribePartialFailure,
            cancellationToken);

    public Task UnsubscribeFromTopic(IReadOnlyCollection<string> tokens, string topic, CancellationToken cancellationToken = default) =>
        ChangeTopicSubscription(
            tokens,
            topic,
            (messaging, tokenBatch) => messaging.UnsubscribeFromTopicAsync(tokenBatch, topic),
            "FCM rejected some tokens while unsubscribing them from a topic.",
            FcmLogEvents.TopicUnsubscribePartialFailure,
            cancellationToken);

    private async Task<IReadOnlyList<FcmSendResult>> SendInBatches(
        IReadOnlyCollection<string> tokens,
        Func<List<string>, MulticastMessage> buildMulticast,
        CancellationToken cancellationToken)
    {
        if (tokens.Count == 0 || IsDisabled(tokens.Count))
        {
            return [];
        }

        var messaging = _firebaseAppProvider.GetMessaging();
        var sendResults = new List<FcmSendResult>(tokens.Count);
        foreach (var tokenBatch in tokens.Chunk(MAX_TOKENS_PER_MULTICAST))
        {
            var batchTokens = tokenBatch.ToList();
            var batchResponse = await messaging
                .SendEachForMulticastAsync(buildMulticast(batchTokens), cancellationToken)
                .ConfigureAwait(false);
            sendResults.AddRange(BuildSendResults(batchTokens, batchResponse));
        }

        LogSendFailures(sendResults);
        await ReportInvalidTokens(sendResults, cancellationToken).ConfigureAwait(false);
        return sendResults;
    }

    private async Task ChangeTopicSubscription(
        IReadOnlyCollection<string> tokens,
        string topic,
        Func<FirebaseMessaging, IReadOnlyList<string>, Task<TopicManagementResponse>> changeSubscription,
        string failureMessage,
        string failureEventName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (tokens.Count == 0 || IsDisabled(tokens.Count))
        {
            return;
        }

        var messaging = _firebaseAppProvider.GetMessaging();
        var successCount = 0;
        var failures = new List<Dictionary<string, object?>>();
        foreach (var tokenBatch in tokens.Chunk(MAX_TOKENS_PER_TOPIC_REQUEST))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var topicResponse = await changeSubscription(messaging, tokenBatch).ConfigureAwait(false);
            successCount += topicResponse.SuccessCount;
            failures.AddRange(topicResponse.Errors.Select(topicError => new Dictionary<string, object?>
            {
                ["reason"] = topicError.Reason,
                ["tokenSuffix"] = ToTokenSuffix(tokenBatch[topicError.Index]),
            }));
        }

        if (failures.Count == 0)
        {
            return;
        }

        _logService.Warn(
            failureMessage,
            new Dictionary<string, object?>
            {
                ["topic"] = topic,
                ["success"] = successCount,
                ["failure"] = failures.Count,
                ["failures"] = failures,
            },
            eventName: failureEventName);
    }

    private bool IsDisabled(int tokenCount)
    {
        if (_fcmOptions.IsEnabled)
        {
            return false;
        }

        _logService.Debug(
            "FCM is disabled; the push was not sent.",
            new Dictionary<string, object?> { ["tokenCount"] = tokenCount },
            eventName: FcmLogEvents.Disabled);
        return true;
    }

    private void LogSendFailures(IReadOnlyList<FcmSendResult> sendResults)
    {
        var failedResults = sendResults.Where(sendResult => !sendResult.IsSuccess).ToList();
        if (failedResults.Count == 0)
        {
            return;
        }

        var failureData = new Dictionary<string, object?>
        {
            ["success"] = sendResults.Count - failedResults.Count,
            ["failure"] = failedResults.Count,
            ["failures"] = failedResults
                .Select(failedResult => new Dictionary<string, object?>
                {
                    ["errorCode"] = failedResult.ErrorCode,
                    ["tokenSuffix"] = ToTokenSuffix(failedResult.Token),
                })
                .ToList(),
        };

        if (failedResults.Exists(failedResult => failedResult.IsProviderCredentialInvalid))
        {
            _logService.Error(
                "FCM could not authenticate against the platform push provider.",
                data: failureData,
                eventName: FcmLogEvents.ProviderCredentialInvalid);
            return;
        }

        _logService.Warn("FCM rejected some tokens.", failureData, eventName: FcmLogEvents.SendPartialFailure);
    }

    private async Task ReportInvalidTokens(IReadOnlyList<FcmSendResult> sendResults, CancellationToken cancellationToken)
    {
        var invalidTokens = sendResults
            .Where(sendResult => sendResult.IsTokenInvalid)
            .Select(sendResult => sendResult.Token)
            .ToList();
        if (invalidTokens.Count == 0)
        {
            return;
        }

        await _invalidTokenHandler.HandleInvalidTokens(invalidTokens, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// FCM answers a multicast in the order the tokens were sent, so the index ties each response to its token.
    /// </summary>
    private static IEnumerable<FcmSendResult> BuildSendResults(IReadOnlyList<string> batchTokens, BatchResponse batchResponse) =>
        batchResponse.Responses.Select((sendResponse, responseIndex) => BuildSendResult(batchTokens[responseIndex], sendResponse));

    private static FcmSendResult BuildSendResult(string token, SendResponse sendResponse)
    {
        var messagingErrorCode = sendResponse.Exception?.MessagingErrorCode;
        return new FcmSendResult
        {
            Token = token,
            IsSuccess = sendResponse.IsSuccess,
            ErrorCode = ResolveErrorCode(sendResponse.Exception, messagingErrorCode),
            ErrorMessage = sendResponse.Exception?.Message,
            IsTokenInvalid = messagingErrorCode is MessagingErrorCode.Unregistered,
            IsProviderCredentialInvalid = messagingErrorCode is MessagingErrorCode.ThirdPartyAuthError,
        };
    }

    private static string? ResolveErrorCode(FirebaseMessagingException? messagingException, MessagingErrorCode? messagingErrorCode)
    {
        if (messagingErrorCode is not null)
        {
            return messagingErrorCode.ToString();
        }

        return messagingException?.ErrorCode.ToString();
    }

    private static MulticastMessage BuildNotificationMulticast(FcmMessage message, List<string> batchTokens) =>
        new()
        {
            Tokens = batchTokens,
            Notification = BuildNotification(message),
            Data = CopyData(message.Data),
            Android = message.Android,
            Apns = message.Apns,
        };

    private MulticastMessage BuildDataOnlyMulticast(IReadOnlyDictionary<string, string> data, List<string> batchTokens)
    {
        var androidConfig = new AndroidConfig { Priority = _fcmOptions.DataOnlyAndroidPriority };
        return new MulticastMessage
        {
            Tokens = batchTokens,
            Data = CopyData(data),
            Android = androidConfig,
        };
    }

    private static Notification BuildNotification(FcmMessage message)
    {
        var imageUrl = string.IsNullOrWhiteSpace(message.ImageUrl) ? null : message.ImageUrl;
        return new Notification { Title = message.Title, Body = message.Body, ImageUrl = imageUrl };
    }

    private static Dictionary<string, string>? CopyData(IReadOnlyDictionary<string, string>? data) =>
        data?.ToDictionary(dataEntry => dataEntry.Key, dataEntry => dataEntry.Value);

    /// <summary>
    /// Device tokens are credentials: only the end is logged, enough to match entries of the same device,
    /// not enough to send a push with.
    /// </summary>
    private static string ToTokenSuffix(string token) =>
        token.Length <= TOKEN_SUFFIX_LENGTH ? token : token[^TOKEN_SUFFIX_LENGTH..];
}
