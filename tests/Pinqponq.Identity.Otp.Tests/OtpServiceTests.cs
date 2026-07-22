using FluentAssertions;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.Otp;
using Xunit;

namespace Pinqponq.Identity.Otp.Tests;

public sealed class OtpServiceTests
{
    private readonly InMemoryOtpStore _store = new();
    private readonly CapturingSmsSender _sms = new();
    private readonly CapturingEmailSender _email = new();

    private OtpService Create(Action<OtpOptions>? configure = null)
    {
        var options = new OtpOptions();
        configure?.Invoke(options);
        return new OtpService(_store, _sms, _email, Options.Create(options));
    }

    [Fact]
    public async Task Auto_channel_routes_email_recipient_to_email()
    {
        var service = Create();

        await service.GenerateAndSendAsync("user@example.com");

        _email.Sent.Should().ContainSingle();
        _sms.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Auto_channel_routes_phone_recipient_to_sms()
    {
        var service = Create();

        await service.GenerateAndSendAsync("+905551112233");

        _sms.Sent.Should().ContainSingle();
        _email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Generated_code_verifies_successfully()
    {
        var service = Create(o => o.CodeLength = 6);
        await service.GenerateAndSendAsync("+905551112233");
        var code = ExtractCode(_sms.Sent[0].Text);

        var result = await service.VerifyAsync("+905551112233", code);

        result.Should().Be(OtpVerifyStatus.Success);
        code.Should().HaveLength(6);
    }

    [Fact]
    public async Task Wrong_code_returns_mismatch_then_success_is_single_use()
    {
        var service = Create();
        await service.GenerateAndSendAsync("+905551112233");
        var code = ExtractCode(_sms.Sent[0].Text);

        (await service.VerifyAsync("+905551112233", "000000")).Should().Be(OtpVerifyStatus.Mismatch);
        (await service.VerifyAsync("+905551112233", code)).Should().Be(OtpVerifyStatus.Success);
        // Consumed on success — a second verify finds nothing.
        (await service.VerifyAsync("+905551112233", code)).Should().Be(OtpVerifyStatus.NotFound);
    }

    [Fact]
    public async Task Exceeding_attempt_limit_returns_too_many_attempts()
    {
        var service = Create(o => o.MaxAttempts = 2);
        await service.GenerateAndSendAsync("+905551112233");

        await service.VerifyAsync("+905551112233", "111111");
        await service.VerifyAsync("+905551112233", "222222");

        (await service.VerifyAsync("+905551112233", "333333")).Should().Be(OtpVerifyStatus.TooManyAttempts);
    }

    [Fact]
    public async Task Expired_code_returns_expired()
    {
        var service = Create(o => o.Ttl = TimeSpan.FromMilliseconds(1));
        await service.GenerateAndSendAsync("+905551112233");
        var code = ExtractCode(_sms.Sent[0].Text);
        await Task.Delay(20);

        (await service.VerifyAsync("+905551112233", code)).Should().Be(OtpVerifyStatus.Expired);
    }

    [Fact]
    public async Task Explicit_sms_channel_overrides_email_recipient()
    {
        var service = Create();

        await service.GenerateAndSendAsync("user@example.com", OtpChannel.Sms);

        _sms.Sent.Should().ContainSingle();
        _email.Sent.Should().BeEmpty();
    }

    private static string ExtractCode(string text) =>
        new(text.Where(char.IsDigit).ToArray());
}
