using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Pinqponq.Identity.Jwt;
using Xunit;

namespace Pinqponq.Identity.Tests.Jwt;

public sealed class JwtTokenTests
{
    private const string HmacKey = "0123456789abcdef0123456789abcdef"; // 32 bytes.

    private static (JwtTokenGenerator gen, JwtTokenValidator val, JwtSigningKeyResolver resolver) Build(
        JwtOptions options)
    {
        var wrapped = Options.Create(options);
        var resolver = new JwtSigningKeyResolver(options);
        return (new JwtTokenGenerator(wrapped, resolver), new JwtTokenValidator(wrapped, resolver), resolver);
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
        var (gen, val, resolver) = Build(HmacOptions());
        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

            var principal = await val.ValidateAsync(token);

            principal.Should().NotBeNull();
            principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("user-1");
        }
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

        var (gen, val, resolver) = Build(options);
        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-9")]);

            var principal = await val.ValidateAsync(token);

            principal.Should().NotBeNull();
            principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("user-9");
        }
    }

    [Fact]
    public async Task Rsa_keys_are_reused_across_operations()
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

        using var resolver = new JwtSigningKeyResolver(options);
        var signingA = resolver.CreateSigningCredentials().Key;
        var signingB = resolver.CreateSigningCredentials().Key;
        var validationA = resolver.CreateValidationKey();
        var validationB = resolver.CreateValidationKey();

        ReferenceEquals(signingA, signingB).Should().BeTrue();
        ReferenceEquals(validationA, validationB).Should().BeTrue();
    }

    [Fact]
    public async Task Token_signed_with_different_key_is_rejected()
    {
        var (gen, _, resolver) = Build(HmacOptions());
        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

            var otherOptions = HmacOptions();
            otherOptions.SymmetricKey = "ffffffffffffffffffffffffffffffff"; // different 32-byte key.
            var (_, otherVal, otherResolver) = Build(otherOptions);
            using (otherResolver)
            {
                (await otherVal.ValidateAsync(token)).Should().BeNull();
            }
        }
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var options = HmacOptions();
        options.Lifetime = TimeSpan.FromMinutes(1);
        options.ClockSkew = TimeSpan.Zero;
        var (gen, val, resolver) = Build(options);
        using (resolver)
        {
            var token = gen.GenerateToken(
                [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                issuedAt: DateTimeOffset.UtcNow.AddHours(-1));

            (await val.ValidateAsync(token)).Should().BeNull();
        }
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        var (gen, _, resolver) = Build(HmacOptions());
        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

            var otherAudience = HmacOptions();
            otherAudience.Audience = "someone-else";
            var (_, val, otherResolver) = Build(otherAudience);
            using (otherResolver)
            {
                (await val.ValidateAsync(token)).Should().BeNull();
            }
        }
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var (gen, val, resolver) = Build(HmacOptions());
        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

            // Replace the signature segment (last-char flip is flaky: base64url padding bits).
            var parts = token.Split('.');
            var tampered = parts[0] + "." + parts[1] + ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

            (await val.ValidateAsync(tampered)).Should().BeNull();
        }
    }

    [Fact]
    public async Task Null_or_empty_token_returns_null()
    {
        var (_, val, resolver) = Build(HmacOptions());
        using (resolver)
        {
            (await val.ValidateAsync(string.Empty)).Should().BeNull();
            (await val.ValidateAsync("   ")).Should().BeNull();
        }
    }

    [Fact]
    public void GenerateToken_emits_jti_when_missing()
    {
        var (gen, _, resolver) = Build(HmacOptions());
        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);
            var jwt = new JsonWebToken(token);
            jwt.GetPayloadValue<string>(JwtRegisteredClaimNames.Jti).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task ValidateAsync_returns_null_when_jti_revoked()
    {
        var options = Options.Create(HmacOptions());
        var resolver = new JwtSigningKeyResolver(options.Value);
        var store = new InMemoryRevocationStore();
        var gen = new JwtTokenGenerator(options, resolver);
        var val = new JwtTokenValidator(options, resolver, store);

        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);
            var jwt = new JsonWebToken(token);
            var jti = jwt.GetPayloadValue<string>(JwtRegisteredClaimNames.Jti)!;

            await store.RevokeAsync(jti, DateTimeOffset.UtcNow.AddHours(1));

            (await val.ValidateAsync(token)).Should().BeNull();
        }
    }

    [Fact]
    public async Task RevokeAccessTokenAsync_marks_jti_revoked()
    {
        var options = Options.Create(HmacOptions());
        var resolver = new JwtSigningKeyResolver(options.Value);
        var store = new InMemoryRevocationStore();
        var gen = new JwtTokenGenerator(options, resolver);
        var val = new JwtTokenValidator(options, resolver, store);
        var revocation = new AccessTokenRevocationService(store, options, resolver);

        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);
            (await val.ValidateAsync(token)).Should().NotBeNull();

            await revocation.RevokeAccessTokenAsync(token);

            (await val.ValidateAsync(token)).Should().BeNull();
        }
    }

    [Fact]
    public async Task RevokeAccessTokenAsync_ignores_tampered_token()
    {
        var options = Options.Create(HmacOptions());
        var resolver = new JwtSigningKeyResolver(options.Value);
        var store = new InMemoryRevocationStore();
        var gen = new JwtTokenGenerator(options, resolver);
        var revocation = new AccessTokenRevocationService(store, options, resolver);

        using (resolver)
        {
            var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-1")]);
            var jwt = new JsonWebToken(token);
            var jti = jwt.GetPayloadValue<string>(JwtRegisteredClaimNames.Jti)!;
            var parts = token.Split('.');
            var badSignature = parts[0] + "." + parts[1] + ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

            await revocation.RevokeAccessTokenAsync(badSignature);

            (await store.IsRevokedAsync(jti)).Should().BeFalse();
        }
    }

    private sealed class InMemoryRevocationStore : IAccessTokenRevocationStore
    {
        private readonly HashSet<string> _revoked = [];

        public Task RevokeAsync(
            string jti,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            _revoked.Add(jti);
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            Task.FromResult(_revoked.Contains(jti));
    }
}
