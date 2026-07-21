using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.Jwt;
using Xunit;

namespace Pinqponq.Identity.Tests.Jwt;

public sealed class JwtTokenTests
{
    private const string HmacKey = "0123456789abcdef0123456789abcdef"; // 32 bytes.

    private static (JwtTokenGenerator gen, JwtTokenValidator val) Build(JwtOptions options)
    {
        var wrapped = Options.Create(options);
        var resolver = new JwtSigningKeyResolver(options);
        return (new JwtTokenGenerator(wrapped, resolver), new JwtTokenValidator(wrapped, resolver));
    }

    private static JwtOptions HmacOptions() => new()
    {
        Issuer = "pinqponq",
        Audience = "clients",
        Algorithm = JwtSigningAlgorithm.HmacSha256,
        SymmetricKey = HmacKey,
    };

    [Fact]
    public async Task Hmac_roundtrip_yields_expected_claims()
    {
        var (gen, val) = Build(HmacOptions());
        var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

        var principal = await val.ValidateAsync(token);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("user-1");
    }

    [Fact]
    public async Task Rsa_roundtrip_yields_expected_claims()
    {
        using var rsa = RSA.Create(2048);
        var options = new JwtOptions
        {
            Issuer = "pinqponq",
            Audience = "clients",
            Algorithm = JwtSigningAlgorithm.RsaSha256,
            RsaPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
            RsaPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
        };

        var (gen, val) = Build(options);
        var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-9")]);

        var principal = await val.ValidateAsync(token);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("user-9");
    }

    [Fact]
    public async Task Token_signed_with_different_key_is_rejected()
    {
        var (gen, _) = Build(HmacOptions());
        var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

        var otherOptions = HmacOptions();
        otherOptions.SymmetricKey = "ffffffffffffffffffffffffffffffff"; // different 32-byte key.
        var (_, otherVal) = Build(otherOptions);

        (await otherVal.ValidateAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var options = HmacOptions();
        options.Lifetime = TimeSpan.FromMinutes(1);
        options.ClockSkew = TimeSpan.Zero;
        var (gen, val) = Build(options);

        var token = gen.GenerateToken(
            [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            issuedAt: DateTimeOffset.UtcNow.AddHours(-1));

        (await val.ValidateAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        var (gen, _) = Build(HmacOptions());
        var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

        var otherAudience = HmacOptions();
        otherAudience.Audience = "someone-else";
        var (_, val) = Build(otherAudience);

        (await val.ValidateAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var (gen, val) = Build(HmacOptions());
        var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

        // Flip the last character of the signature segment.
        var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        (await val.ValidateAsync(tampered)).Should().BeNull();
    }

    [Fact]
    public async Task Null_or_empty_token_returns_null()
    {
        var (_, val) = Build(HmacOptions());

        (await val.ValidateAsync(string.Empty)).Should().BeNull();
        (await val.ValidateAsync("   ")).Should().BeNull();
    }
}
