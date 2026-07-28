using FluentAssertions;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.Otp;
using Xunit;

namespace Pinqponq.Identity.Otp.Tests;

public sealed class OtpServiceTests
{
    private const string TestPepper = "0123456789abcdef0123456789abcdef";

    private readonly InMemoryOtpStore _store = new();
    private readonly CapturingSmsSender _sms = new();
    private readonly CapturingEmailSender _email = new();

    private OtpService Create(
        Action<OtpOptions>? configure = null,
        IOtpSendRateLimiter? rateLimiter = null)
    {
        var options = new OtpOptions { HashPepper = TestPepper };
        configure?.Invoke(options);
        options.HashPepper = string.IsNullOrEmpty(options.HashPepper) ? TestPepper : options.HashPepper;
        return new OtpService(
            _store,
            _sms,
            _email,
            rateLimiter ?? new AllowAllOtpSendRateLimiter(),
            Options.Create(options));
    }

    [Fact]
    public async Task GenerateAndSendAsync_rate_limited_throws()
    {
        var service = Create(
            o => o.MinSendInterval = TimeSpan.FromSeconds(30),
            rateLimiter: new DenyAllOtpSendRateLimiter());

        var act = () => service.GenerateAndSendAsync("+905551112233");
        await act.Should().ThrowAsync<OtpSendRateLimitedException>();
        _sms.Sent.Should().BeEmpty();
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task GenerateAndSendAsync_allow_limiter_sends()
    {
        var service = Create(
            o => o.MinSendInterval = TimeSpan.FromSeconds(30),
            rateLimiter: new AllowAllOtpSendRateLimiter());

        await service.GenerateAndSendAsync("+905551112233");
        _sms.Sent.Should().ContainSingle();
    }

    private sealed class DenyAllOtpSendRateLimiter : IOtpSendRateLimiter
    {
        public Task<bool> TryAcquireAsync(
            string key,
            TimeSpan minInterval,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
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

    [Fact]
    public async Task Empty_code_returns_mismatch()
    {
        var service = Create();
        await service.GenerateAndSendAsync("+905551112233");

        (await service.VerifyAsync("+905551112233", "  ")).Should().Be(OtpVerifyStatus.Mismatch);
        (await service.VerifyAsync("+905551112233", null!)).Should().Be(OtpVerifyStatus.Mismatch);
    }

    [Fact]
    public async Task Send_failure_removes_stored_code()
    {
        _sms.ThrowOnSend = new InvalidOperationException("smtp/sms down");
        var service = Create();

        var act = () => service.GenerateAndSendAsync("+905551112233");
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await service.VerifyAsync("+905551112233", "000000")).Should().Be(OtpVerifyStatus.NotFound);
    }

    [Fact]
    public async Task TryRemoveAsync_does_not_remove_newer_code_with_stale_hash()
    {
        const string key = "otp:test";
        await _store.SaveAsync(new OtpRecord
        {
            Key = key,
            CodeHash = "OLD",
            Recipient = "r",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        await _store.SaveAsync(new OtpRecord
        {
            Key = key,
            CodeHash = "NEW",
            Recipient = "r",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
        });

        (await _store.TryRemoveAsync(key, "OLD")).Should().BeFalse();
        _store.SingleRecord().CodeHash.Should().Be("NEW");
        (await _store.TryRemoveAsync(key, "NEW")).Should().BeTrue();
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task Purpose_recipient_delimiter_does_not_collide()
    {
        var service = Create();
        await service.GenerateAndSendAsync("bar:baz", purpose: "foo");
        var code1 = ExtractCode(_sms.Sent[^1].Text);

        await service.GenerateAndSendAsync("baz", purpose: "foo:bar");
        var code2 = ExtractCode(_sms.Sent[^1].Text);

        (await service.VerifyAsync("bar:baz", code1, purpose: "foo")).Should().Be(OtpVerifyStatus.Success);
        (await service.VerifyAsync("baz", code2, purpose: "foo:bar")).Should().Be(OtpVerifyStatus.Success);
    }

    [Fact]
    public async Task Different_peppers_produce_different_hashes()
    {
        var serviceA = Create(o => o.HashPepper = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        await serviceA.GenerateAndSendAsync("+905551112233");
        var hashA = _store.SingleRecord().CodeHash;
        var key = _store.SingleRecord().Key;
        await _store.RemoveAsync(key);

        var serviceB = Create(o => o.HashPepper = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        await serviceB.GenerateAndSendAsync("+905551112233");
        var hashB = _store.SingleRecord().CodeHash;

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public async Task Concurrent_correct_verifies_yield_single_success()
    {
        var service = Create();
        await service.GenerateAndSendAsync("+905551112233");
        var code = ExtractCode(_sms.Sent[0].Text);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => service.VerifyAsync("+905551112233", code))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        results.Count(r => r == OtpVerifyStatus.Success).Should().Be(1);
        results.Count(r => r is OtpVerifyStatus.NotFound or OtpVerifyStatus.Success)
            .Should().Be(20);
    }

    private static string ExtractCode(string text) =>
        new(text.Where(char.IsDigit).ToArray());
}
