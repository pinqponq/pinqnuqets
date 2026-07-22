using FluentAssertions;
using Microsoft.Extensions.Options;
using Pinqponq.Auth.Totp;
using Xunit;

namespace Pinqponq.Auth.Totp.Tests;

public sealed class TotpServiceTests
{
    // RFC 6238 Appendix B test seed: ASCII "12345678901234567890" in Base32.
    private const string Rfc6238Sha1Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static TotpService Create(Action<TotpOptions>? configure = null)
    {
        var options = new TotpOptions();
        configure?.Invoke(options);
        return new TotpService(Options.Create(options));
    }

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    public void ComputeCode_matches_rfc6238_sha1_vectors(long unixSeconds, string expected)
    {
        var totp = Create(o =>
        {
            o.Digits = 8;
            o.Algorithm = TotpAlgorithm.Sha1;
            o.PeriodSeconds = 30;
        });

        var at = DateTimeOffset.UnixEpoch.AddSeconds(unixSeconds);
        totp.ComputeCode(Rfc6238Sha1Secret, at).Should().Be(expected);
    }

    [Fact]
    public void Validate_accepts_current_code()
    {
        var totp = Create();
        var secret = totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        var code = totp.ComputeCode(secret, now);

        totp.Validate(secret, code, now).Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_wrong_code()
    {
        var totp = Create();
        var secret = totp.GenerateSecret();

        totp.Validate(secret, "000000").Should().BeFalse();
    }

    [Fact]
    public void Validate_tolerates_one_step_drift_within_window()
    {
        var totp = Create(o => o.ValidationWindow = 1);
        var secret = totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        // Code from the previous 30s step should still validate with window = 1.
        var previousStepCode = totp.ComputeCode(secret, now.AddSeconds(-30));

        totp.Validate(secret, previousStepCode, now).Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_code_outside_window()
    {
        var totp = Create(o => o.ValidationWindow = 0);
        var secret = totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        var farCode = totp.ComputeCode(secret, now.AddSeconds(-120));

        totp.Validate(secret, farCode, now).Should().BeFalse();
    }

    [Fact]
    public void GenerateSecret_produces_decodable_secret_of_expected_length()
    {
        var totp = Create(o => o.SecretByteLength = 20);

        var secret = totp.GenerateSecret();

        Base32.Decode(secret).Should().HaveCount(20);
    }

    [Fact]
    public void GetProvisioningUri_has_expected_shape()
    {
        var totp = Create(o =>
        {
            o.Issuer = "Pinqponq";
            o.Digits = 6;
            o.PeriodSeconds = 30;
        });

        var uri = totp.GetProvisioningUri("JBSWY3DPEHPK3PXP", "user@example.com");

        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("secret=JBSWY3DPEHPK3PXP");
        uri.Should().Contain("issuer=Pinqponq");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
        uri.Should().Contain("algorithm=SHA1");
    }
}
