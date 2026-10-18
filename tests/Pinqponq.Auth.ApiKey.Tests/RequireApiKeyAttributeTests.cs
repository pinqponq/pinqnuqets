using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Pinqponq.Auth.ApiKey.Tests;

public sealed class RequireApiKeyAttributeTests
{
    private const string INTERNAL_NAME = "internal";
    private const string INTERNAL_HEADER = "X-Internal-Key";
    private const string INTERNAL_KEY = "internal-s3cret";

    [Fact]
    public async Task Valid_key_runs_the_action()
    {
        var harness = new ApiKeyFilterHarness().WithKey(INTERNAL_NAME, INTERNAL_HEADER, INTERNAL_KEY);

        var result = await harness.Send(INTERNAL_NAME, new Dictionary<string, string> { [INTERNAL_HEADER] = INTERNAL_KEY });

        Assert.Null(result);
        Assert.True(harness.IsActionExecuted);
    }

    [Theory]
    [InlineData("internal-s3creT")]
    [InlineData("internal-s3cre")]
    [InlineData("")]
    public async Task Wrong_or_empty_header_returns_401(string providedKey)
    {
        var harness = new ApiKeyFilterHarness().WithKey(INTERNAL_NAME, INTERNAL_HEADER, INTERNAL_KEY);

        var result = await harness.Send(INTERNAL_NAME, new Dictionary<string, string> { [INTERNAL_HEADER] = providedKey });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(harness.IsActionExecuted);
    }

    [Fact]
    public async Task Missing_header_returns_401()
    {
        var harness = new ApiKeyFilterHarness().WithKey(INTERNAL_NAME, INTERNAL_HEADER, INTERNAL_KEY);

        var result = await harness.Send(INTERNAL_NAME);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(harness.IsActionExecuted);
    }

    [Fact]
    public async Task Key_in_another_header_returns_401()
    {
        var harness = new ApiKeyFilterHarness().WithKey(INTERNAL_NAME, INTERNAL_HEADER, INTERNAL_KEY);

        var result = await harness.Send(INTERNAL_NAME, new Dictionary<string, string> { ["X-Other-Key"] = INTERNAL_KEY });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(harness.IsActionExecuted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Unset_configured_key_returns_401_and_logs_a_warning(string? configuredKey)
    {
        var harness = new ApiKeyFilterHarness().WithKey(INTERNAL_NAME, INTERNAL_HEADER, configuredKey);

        var result = await harness.Send(INTERNAL_NAME, new Dictionary<string, string> { [INTERNAL_HEADER] = string.Empty });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(harness.IsActionExecuted);
        var entry = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("'internal'", entry.Message);
    }

    [Fact]
    public async Task Unregistered_name_returns_401()
    {
        var harness = new ApiKeyFilterHarness().WithKey(INTERNAL_NAME, INTERNAL_HEADER, INTERNAL_KEY);

        var result = await harness.Send("unknown", new Dictionary<string, string> { [INTERNAL_HEADER] = INTERNAL_KEY });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(harness.IsActionExecuted);
    }

    [Fact]
    public async Task Named_keys_do_not_accept_each_other()
    {
        var harness = new ApiKeyFilterHarness()
            .WithKey("media", "X-Shared-Key", "media-s3cret")
            .WithKey("notification", "X-Shared-Key", "notification-s3cret");

        var mediaWithNotificationKey = await harness.Send("media", new Dictionary<string, string> { ["X-Shared-Key"] = "notification-s3cret" });
        var notificationWithMediaKey = await harness.Send("notification", new Dictionary<string, string> { ["X-Shared-Key"] = "media-s3cret" });

        Assert.IsType<UnauthorizedResult>(mediaWithNotificationKey);
        Assert.IsType<UnauthorizedResult>(notificationWithMediaKey);
        Assert.False(harness.IsActionExecuted);
        Assert.Null(await harness.Send("media", new Dictionary<string, string> { ["X-Shared-Key"] = "media-s3cret" }));
        Assert.Null(await harness.Send("notification", new Dictionary<string, string> { ["X-Shared-Key"] = "notification-s3cret" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_name_is_rejected(string name) =>
        Assert.ThrowsAny<ArgumentException>(() => new RequireApiKeyAttribute(name));

    [Fact]
    public void Null_name_is_rejected() => Assert.Throws<ArgumentNullException>(() => new RequireApiKeyAttribute(null!));

    [Fact]
    public void Name_is_kept() => Assert.Equal(INTERNAL_NAME, new RequireApiKeyAttribute(INTERNAL_NAME).Name);
}
