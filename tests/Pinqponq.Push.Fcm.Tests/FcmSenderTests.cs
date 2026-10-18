using FirebaseAdmin.Messaging;
using Xunit;

namespace Pinqponq.Push.Fcm.Tests;

public sealed class FcmSenderTests
{
    private static readonly FcmMessage Message = new() { Title = "Hello", Body = "World" };

    [Fact]
    public async Task SendToTokens_returns_a_success_result_per_token_in_the_given_order()
    {
        using var app = new FcmTestApp();

        var results = await app.Sender.SendToTokens(Message, ["token-a", "token-b", "token-c"]);

        Assert.Equal(["token-a", "token-b", "token-c"], results.Select(result => result.Token));
        Assert.All(results, result =>
        {
            Assert.True(result.IsSuccess);
            Assert.Null(result.ErrorCode);
            Assert.Null(result.ErrorMessage);
            Assert.False(result.IsTokenInvalid);
            Assert.False(result.IsProviderCredentialInvalid);
        });
        Assert.Empty(app.LogService.Entries);
        Assert.Empty(app.InvalidTokenHandler.Calls);
    }

    [Fact]
    public async Task SendToTokens_marks_an_unregistered_token_invalid_and_passes_it_to_the_handler()
    {
        using var app = new FcmTestApp();
        app.Server.FailSend("token-invalid", FakeFcmError.Unregistered);

        var results = await app.Sender.SendToTokens(Message, ["token-alive", "token-invalid"]);

        var invalidResult = results[1];
        Assert.False(invalidResult.IsSuccess);
        Assert.True(invalidResult.IsTokenInvalid);
        Assert.False(invalidResult.IsProviderCredentialInvalid);
        Assert.Equal(nameof(MessagingErrorCode.Unregistered), invalidResult.ErrorCode);
        Assert.Contains("NOT_FOUND", invalidResult.ErrorMessage);
        Assert.True(results[0].IsSuccess);
        var handlerCall = Assert.Single(app.InvalidTokenHandler.Calls);
        Assert.Equal(["token-invalid"], handlerCall);
    }

    [Fact]
    public async Task SendToTokens_marks_a_third_party_auth_error_as_a_provider_credential_problem_and_logs_an_error()
    {
        using var app = new FcmTestApp();
        app.Server.FailSend("token-ios", FakeFcmError.ThirdPartyAuth);

        var results = await app.Sender.SendToTokens(Message, ["token-ios"]);

        var result = Assert.Single(results);
        Assert.False(result.IsSuccess);
        Assert.True(result.IsProviderCredentialInvalid);
        Assert.False(result.IsTokenInvalid);
        Assert.Equal(nameof(MessagingErrorCode.ThirdPartyAuthError), result.ErrorCode);
        Assert.Empty(app.InvalidTokenHandler.Calls);
        var logEntry = Assert.Single(app.LogService.Entries);
        Assert.Equal("Error", logEntry.Level);
        Assert.Equal("FCM could not authenticate against the platform push provider.", logEntry.Message);
        Assert.Equal(FcmLogEvents.ProviderCredentialInvalid, logEntry.EventName);
    }

    [Theory]
    [InlineData(nameof(FakeFcmError.InvalidArgument), nameof(MessagingErrorCode.InvalidArgument))]
    [InlineData(nameof(FakeFcmError.SenderIdMismatch), nameof(MessagingErrorCode.SenderIdMismatch))]
    public async Task SendToTokens_does_not_mark_a_token_invalid_for_errors_that_can_come_from_the_request(string errorName, string expectedErrorCode)
    {
        using var app = new FcmTestApp();
        var error = errorName == nameof(FakeFcmError.InvalidArgument) ? FakeFcmError.InvalidArgument : FakeFcmError.SenderIdMismatch;
        app.Server.FailSend("token-a", error);

        var results = await app.Sender.SendToTokens(Message, ["token-a"]);

        var result = Assert.Single(results);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsTokenInvalid);
        Assert.False(result.IsProviderCredentialInvalid);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Empty(app.InvalidTokenHandler.Calls);
    }

    [Fact]
    public async Task SendToTokens_falls_back_to_the_general_firebase_error_code()
    {
        using var app = new FcmTestApp();
        app.Server.FailSend("token-a", FakeFcmError.PermissionDenied);

        var results = await app.Sender.SendToTokens(Message, ["token-a"]);

        Assert.Equal("PermissionDenied", Assert.Single(results).ErrorCode);
    }

    [Fact]
    public async Task SendToTokens_logs_failures_as_a_warning_with_only_the_token_suffix()
    {
        using var app = new FcmTestApp();
        app.Server.FailSend("secret-token-123456", FakeFcmError.Unregistered);

        await app.Sender.SendToTokens(Message, ["token-alive", "secret-token-123456"]);

        var logEntry = Assert.Single(app.LogService.Entries);
        Assert.Equal("Warn", logEntry.Level);
        Assert.Equal("FCM rejected some tokens.", logEntry.Message);
        Assert.Equal(FcmLogEvents.SendPartialFailure, logEntry.EventName);
        Assert.Equal(1, logEntry.Data!["success"]);
        Assert.Equal(1, logEntry.Data["failure"]);
        var failure = Assert.Single((List<Dictionary<string, object?>>)logEntry.Data["failures"]!);
        Assert.Equal("123456", failure["tokenSuffix"]);
        Assert.Equal(nameof(MessagingErrorCode.Unregistered), failure["errorCode"]);
    }

    [Fact]
    public async Task SendToTokens_logs_a_token_of_exactly_the_suffix_length_whole()
    {
        using var app = new FcmTestApp();
        app.Server.FailSend("abcdef", FakeFcmError.Unregistered);

        await app.Sender.SendToTokens(Message, ["abcdef"]);

        var failure = Assert.Single((List<Dictionary<string, object?>>)app.LogService.Entries[0].Data!["failures"]!);
        Assert.Equal("abcdef", failure["tokenSuffix"]);
    }

    [Fact]
    public async Task SendToTokens_logs_a_short_token_whole()
    {
        using var app = new FcmTestApp();
        app.Server.FailSend("abc", FakeFcmError.Unregistered);

        await app.Sender.SendToTokens(Message, ["abc"]);

        var failure = Assert.Single((List<Dictionary<string, object?>>)app.LogService.Entries[0].Data!["failures"]!);
        Assert.Equal("abc", failure["tokenSuffix"]);
    }

    [Fact]
    public async Task SendToTokens_sends_the_notification_data_and_platform_settings()
    {
        using var app = new FcmTestApp();
        var message = new FcmMessage
        {
            Title = "Title",
            Body = "Body",
            ImageUrl = "https://example.com/image.png",
            Data = new Dictionary<string, string> { ["key"] = "value" },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig { Aps = new Aps { Sound = "default" } },
        };

        await app.Sender.SendToTokens(message, ["token-a"]);

        var sentMessage = Assert.Single(app.Server.SentMessages);
        Assert.Equal("token-a", sentMessage["token"]!.GetValue<string>());
        Assert.Equal("Title", sentMessage["notification"]!["title"]!.GetValue<string>());
        Assert.Equal("Body", sentMessage["notification"]!["body"]!.GetValue<string>());
        Assert.Equal("https://example.com/image.png", sentMessage["notification"]!["image"]!.GetValue<string>());
        Assert.Equal("value", sentMessage["data"]!["key"]!.GetValue<string>());
        Assert.Equal("high", sentMessage["android"]!["priority"]!.GetValue<string>());
        Assert.Equal("default", sentMessage["apns"]!["payload"]!["aps"]!["sound"]!.GetValue<string>());
    }

    [Fact]
    public async Task SendToTokens_leaves_out_a_blank_image_and_unset_platform_settings()
    {
        using var app = new FcmTestApp();

        await app.Sender.SendToTokens(new FcmMessage { Title = "Title", Body = "Body", ImageUrl = " " }, ["token-a"]);

        var sentMessage = Assert.Single(app.Server.SentMessages);
        Assert.Null(sentMessage["notification"]!["image"]);
        Assert.Null(sentMessage["data"]);
        Assert.Null(sentMessage["android"]);
        Assert.Null(sentMessage["apns"]);
    }

    [Fact]
    public async Task SendToTokens_splits_more_than_five_hundred_tokens_into_several_multicasts()
    {
        using var app = new FcmTestApp();
        var tokens = Enumerable.Range(0, 501).Select(tokenNumber => $"token-{tokenNumber}").ToList();
        app.Server.FailSend("token-500", FakeFcmError.Unregistered);

        var results = await app.Sender.SendToTokens(Message, tokens);

        Assert.Equal(tokens, results.Select(result => result.Token));
        Assert.Equal(501, app.Server.SentMessages.Count);
        Assert.True(results[500].IsTokenInvalid);
        Assert.Equal(["token-500"], Assert.Single(app.InvalidTokenHandler.Calls));
        Assert.Equal(500, app.LogService.Entries[0].Data!["success"]);
    }

    [Fact]
    public async Task SendToTokens_with_no_tokens_sends_nothing_and_does_not_create_the_firebase_app()
    {
        using var app = new FcmTestApp();

        var results = await app.Sender.SendToTokens(Message, []);

        Assert.Empty(results);
        Assert.Empty(app.Server.SentMessages);
        Assert.False(app.IsAppCreated);
        Assert.Empty(app.LogService.Entries);
    }

    [Fact]
    public async Task SendToTokens_when_disabled_sends_nothing_and_logs_a_debug_record()
    {
        using var app = new FcmTestApp(options => options.IsEnabled = false);

        var results = await app.Sender.SendToTokens(Message, ["token-a", "token-b"]);

        Assert.Empty(results);
        Assert.Empty(app.Server.SentMessages);
        Assert.False(app.IsAppCreated);
        var logEntry = Assert.Single(app.LogService.Entries);
        Assert.Equal("Debug", logEntry.Level);
        Assert.Equal("FCM is disabled, so the push was not sent.", logEntry.Message);
        Assert.Equal(FcmLogEvents.Disabled, logEntry.EventName);
        Assert.Equal(2, logEntry.Data!["tokenCount"]);
    }

    [Fact]
    public async Task SendToTokens_rejects_null_arguments()
    {
        using var app = new FcmTestApp();

        await Assert.ThrowsAsync<ArgumentNullException>(() => app.Sender.SendToTokens(null!, ["token-a"]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => app.Sender.SendToTokens(Message, null!));
    }

    [Fact]
    public async Task SendDataOnlyToTokens_sends_data_without_a_notification_at_the_default_normal_priority()
    {
        using var app = new FcmTestApp();

        var results = await app.Sender.SendDataOnlyToTokens(new Dictionary<string, string> { ["type"] = "sync" }, ["token-a"]);

        Assert.True(Assert.Single(results).IsSuccess);
        var sentMessage = Assert.Single(app.Server.SentMessages);
        Assert.Null(sentMessage["notification"]);
        Assert.Equal("sync", sentMessage["data"]!["type"]!.GetValue<string>());
        Assert.Equal("normal", sentMessage["android"]!["priority"]!.GetValue<string>());
    }

    [Fact]
    public async Task SendDataOnlyToTokens_uses_the_configured_android_priority_and_reports_invalid_tokens()
    {
        using var app = new FcmTestApp(options => options.DataOnlyAndroidPriority = Priority.High);
        app.Server.FailSend("token-invalid", FakeFcmError.Unregistered);

        var results = await app.Sender.SendDataOnlyToTokens(new Dictionary<string, string> { ["type"] = "sync" }, ["token-invalid"]);

        Assert.True(Assert.Single(results).IsTokenInvalid);
        Assert.Equal("high", app.Server.SentMessages[0]["android"]!["priority"]!.GetValue<string>());
        Assert.Equal(["token-invalid"], Assert.Single(app.InvalidTokenHandler.Calls));
    }

    [Fact]
    public async Task SendDataOnlyToTokens_rejects_empty_data_and_null_arguments()
    {
        using var app = new FcmTestApp();

        var emptyDataException = await Assert.ThrowsAsync<ArgumentException>(() => app.Sender.SendDataOnlyToTokens(new Dictionary<string, string>(), ["token-a"]));
        Assert.StartsWith("A data-only push needs at least one data entry.", emptyDataException.Message);
        await Assert.ThrowsAsync<ArgumentNullException>(() => app.Sender.SendDataOnlyToTokens(null!, ["token-a"]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => app.Sender.SendDataOnlyToTokens(new Dictionary<string, string> { ["a"] = "b" }, null!));
        Assert.Empty(app.Server.SentMessages);
    }

    [Fact]
    public async Task SendDataOnlyToTokens_when_disabled_sends_nothing()
    {
        using var app = new FcmTestApp(options => options.IsEnabled = false);

        var results = await app.Sender.SendDataOnlyToTokens(new Dictionary<string, string> { ["a"] = "b" }, ["token-a"]);

        Assert.Empty(results);
        Assert.Empty(app.Server.SentMessages);
    }

    [Fact]
    public async Task SendToTopic_sends_the_notification_to_the_topic()
    {
        using var app = new FcmTestApp();
        var message = new FcmMessage
        {
            Title = "Title",
            Body = "Body",
            Data = new Dictionary<string, string> { ["key"] = "value" },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig { Aps = new Aps { ContentAvailable = true } },
        };

        await app.Sender.SendToTopic("news", message);

        var sentMessage = Assert.Single(app.Server.SentMessages);
        Assert.Equal("news", sentMessage["topic"]!.GetValue<string>());
        Assert.Equal("Title", sentMessage["notification"]!["title"]!.GetValue<string>());
        Assert.Equal("value", sentMessage["data"]!["key"]!.GetValue<string>());
        Assert.Equal("high", sentMessage["android"]!["priority"]!.GetValue<string>());
        Assert.Equal(1, sentMessage["apns"]!["payload"]!["aps"]!["content-available"]!.GetValue<int>());
    }

    [Fact]
    public async Task SendToTopic_rejects_a_blank_topic_and_a_null_message()
    {
        using var app = new FcmTestApp();

        await Assert.ThrowsAsync<ArgumentException>(() => app.Sender.SendToTopic(" ", Message));
        await Assert.ThrowsAsync<ArgumentNullException>(() => app.Sender.SendToTopic("news", null!));
        Assert.Empty(app.Server.SentMessages);
    }

    [Fact]
    public async Task SendToTopic_when_disabled_sends_nothing()
    {
        using var app = new FcmTestApp(options => options.IsEnabled = false);

        await app.Sender.SendToTopic("news", Message);

        Assert.Empty(app.Server.SentMessages);
        Assert.Equal(0, Assert.Single(app.LogService.Entries).Data!["tokenCount"]);
    }

    [Fact]
    public async Task SubscribeToTopic_adds_the_tokens_to_the_topic()
    {
        using var app = new FcmTestApp();

        await app.Sender.SubscribeToTopic(["token-a", "token-b"], "news");

        var topicRequest = Assert.Single(app.Server.TopicRequests);
        Assert.EndsWith(":batchAdd", topicRequest.Path);
        Assert.Equal("/topics/news", topicRequest.Topic);
        Assert.Equal(["token-a", "token-b"], topicRequest.Tokens);
        Assert.Empty(app.LogService.Entries);
    }

    [Fact]
    public async Task SubscribeToTopic_logs_rejected_tokens_with_their_reason_and_suffix()
    {
        using var app = new FcmTestApp();
        app.Server.FailTopic("rejected-token-654321", "NOT_FOUND");

        await app.Sender.SubscribeToTopic(["token-a", "rejected-token-654321"], "news");

        var logEntry = Assert.Single(app.LogService.Entries);
        Assert.Equal("Warn", logEntry.Level);
        Assert.Equal("FCM rejected some tokens while subscribing them to a topic.", logEntry.Message);
        Assert.Equal(FcmLogEvents.TopicSubscribePartialFailure, logEntry.EventName);
        Assert.Equal("news", logEntry.Data!["topic"]);
        Assert.Equal(1, logEntry.Data["success"]);
        Assert.Equal(1, logEntry.Data["failure"]);
        var failure = Assert.Single((List<Dictionary<string, object?>>)logEntry.Data["failures"]!);
        Assert.Equal("654321", failure["tokenSuffix"]);
        Assert.False(string.IsNullOrEmpty((string?)failure["reason"]));
    }

    [Fact]
    public async Task SubscribeToTopic_splits_more_than_a_thousand_tokens_into_several_requests()
    {
        using var app = new FcmTestApp();
        var tokens = Enumerable.Range(0, 1001).Select(tokenNumber => $"token-{tokenNumber}").ToList();
        app.Server.FailTopic("token-1000", "INVALID_ARGUMENT");

        await app.Sender.SubscribeToTopic(tokens, "news");

        Assert.Equal([1000, 1], app.Server.TopicRequests.Select(topicRequest => topicRequest.Tokens.Count));
        var logEntry = Assert.Single(app.LogService.Entries);
        Assert.Equal(1000, logEntry.Data!["success"]);
        var failure = Assert.Single((List<Dictionary<string, object?>>)logEntry.Data["failures"]!);
        Assert.Equal("n-1000", failure["tokenSuffix"]);
    }

    [Fact]
    public async Task UnsubscribeFromTopic_removes_the_tokens_and_logs_rejected_ones()
    {
        using var app = new FcmTestApp();
        app.Server.FailTopic("token-b", "NOT_FOUND");

        await app.Sender.UnsubscribeFromTopic(["token-a", "token-b"], "news");

        var topicRequest = Assert.Single(app.Server.TopicRequests);
        Assert.EndsWith(":batchRemove", topicRequest.Path);
        Assert.Equal("/topics/news", topicRequest.Topic);
        var logEntry = Assert.Single(app.LogService.Entries);
        Assert.Equal("FCM rejected some tokens while unsubscribing them from a topic.", logEntry.Message);
        Assert.Equal(FcmLogEvents.TopicUnsubscribePartialFailure, logEntry.EventName);
    }

    [Fact]
    public async Task Topic_changes_with_no_tokens_or_when_disabled_send_nothing()
    {
        using var app = new FcmTestApp();
        using var disabledApp = new FcmTestApp(options => options.IsEnabled = false);

        await app.Sender.SubscribeToTopic([], "news");
        await app.Sender.UnsubscribeFromTopic([], "news");
        await disabledApp.Sender.SubscribeToTopic(["token-a"], "news");
        await disabledApp.Sender.UnsubscribeFromTopic(["token-a"], "news");

        Assert.Empty(app.Server.TopicRequests);
        Assert.Empty(disabledApp.Server.TopicRequests);
        Assert.False(app.IsAppCreated);
        Assert.Equal(2, disabledApp.LogService.Entries.Count);
    }

    [Fact]
    public async Task Topic_changes_reject_a_blank_topic_null_tokens_and_a_cancelled_token()
    {
        using var app = new FcmTestApp();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentException>(() => app.Sender.SubscribeToTopic(["token-a"], " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => app.Sender.UnsubscribeFromTopic(null!, "news"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => app.Sender.SubscribeToTopic(["token-a"], "news", cancellation.Token));
        Assert.Empty(app.Server.TopicRequests);
    }
}
