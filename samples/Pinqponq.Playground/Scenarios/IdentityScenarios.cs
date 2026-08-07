using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Pinqponq.Identity.DependencyInjection;
using Pinqponq.Identity.Jwt;
using Pinqponq.Identity.Passwords;
using Pinqponq.Identity.RefreshTokens;
using Pinqponq.Playground.Scenarios.Support;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Scenarios for <c>Pinqponq.Identity</c>. None of them need Docker.</summary>
public static class IdentityScenarios
{
    private const string Package = "Pinqponq.Identity";
    private const string DemoKey = "playground-symmetric-key-0123456789abcdef";

    private static readonly ScenarioField IssuerField =
        new("issuer", "Issuer (iss)", ScenarioFieldKind.Text, "pinqponq");

    private static readonly ScenarioField AudienceField =
        new("audience", "Audience (aud)", ScenarioFieldKind.Text, "pinqponq-clients");

    private static readonly ScenarioField KeyField =
        new("symmetricKey", "SymmetricKey", ScenarioFieldKind.Password, DemoKey,
            "Must be at least 32 bytes for HMAC.");

    public static IEnumerable<Scenario> Create()
    {
        yield return HmacRoundTrip();
        yield return RsaRoundTrip();
        yield return ExpiredToken();
        yield return WrongAudience();
        yield return ShortKeyRejected();
        yield return JtiIssuedAndRevoked();
        yield return PasswordHashing();
        yield return RefreshTokenRotation();
        yield return RefreshTokenReuseDetected();
        yield return RefreshTokenFamilyRevoke();
        yield return MissingStoreDetected();
    }

    private static Scenario HmacRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.hmac",
            PackageId = Package,
            Title = "Issue and validate a JWT (HMAC-SHA256)",
            Summary = "Issues a token with a claim list, validates it with the same configuration, "
                      + "and shows the decoded header/payload and the returned ClaimsPrincipal.",
            Fields =
            [
                IssuerField,
                AudienceField,
                KeyField,
                new ScenarioField("subject", "sub claim", ScenarioFieldKind.Text, "user-42"),
                new ScenarioField("email", "email claim", ScenarioFieldKind.Text, "user@pinqponq.dev"),
                new ScenarioField("role", "role claim", ScenarioFieldKind.Text, "admin"),
                new ScenarioField("lifetimeMs", "Lifetime (ms)", ScenarioFieldKind.Duration, "900000"),
            ],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = context.Input.Text("issuer");
                jwt.Audience = context.Input.Text("audience");
                jwt.SymmetricKey = context.Input.Text("symmetricKey");
                jwt.Algorithm = JwtSigningAlgorithm.HmacSha256;
                jwt.Lifetime = context.Input.Duration("lifetimeMs");
            }));

            context.Step("Isolated container set up via AddPinqponqIdentity");

            var generator = host.GetRequiredService<IJwtTokenGenerator>();
            var token = generator.GenerateToken(
            [
                new Claim(ClaimTypes.NameIdentifier, context.Input.Text("subject")),
                new Claim(ClaimTypes.Email, context.Input.Text("email")),
                new Claim(ClaimTypes.Role, context.Input.Text("role")),
            ]);

            context.Step("Token issued", $"{token.Length} characters");
            context.Artifact("token", token, "token");
            context.Artifact("decoded token", Presentation.Jwt(token));

            var validator = host.GetRequiredService<IJwtTokenValidator>();
            var principal = await validator.ValidateAsync(token, context.CancellationToken);

            context.Require("Token validated", principal is not null);
            context.Artifact("ClaimsPrincipal", Presentation.Principal(principal!));

            context.Check(
                "sub claim preserved",
                principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value == context.Input.Text("subject"));
            context.Check(
                "role claim preserved",
                principal.FindFirst(ClaimTypes.Role)?.Value == context.Input.Text("role"));
            context.Check(
                "jti generated automatically",
                !string.IsNullOrEmpty(
                    principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                    ?? principal.FindFirst("jti")?.Value));
        });

    private static Scenario RsaRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.rsa",
            PackageId = Package,
            Title = "Issue and validate a JWT (RSA-SHA256)",
            Summary = "Generates a 2048-bit RSA key pair, signs with the private key, validates with the "
                      + "public key. Shows that the token's alg header is RS256.",
            Fields = [IssuerField, AudienceField],
        },
        async context =>
        {
            using var rsa = RSA.Create(2048);
            var privatePem = rsa.ExportPkcs8PrivateKeyPem();
            var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
            context.Step("RSA-2048 key pair generated");

            await using var host = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = context.Input.Text("issuer");
                jwt.Audience = context.Input.Text("audience");
                jwt.Algorithm = JwtSigningAlgorithm.RsaSha256;
                jwt.RsaPrivateKeyPem = privatePem;
                jwt.RsaPublicKeyPem = publicPem;
            }));

            var token = host.GetRequiredService<IJwtTokenGenerator>()
                .GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-rsa")]);

            context.Artifact("token", token, "token");
            context.Artifact("decoded token", Presentation.Jwt(token));
            context.Artifact("public key (PEM)", publicPem, "text");

            var principal = await host.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);

            context.Require("Validated with the public key", principal is not null);
        });

    private static Scenario ExpiredToken() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.expired",
            PackageId = Package,
            Title = "An expired token is rejected",
            Summary = "The token is issued far enough in the past to exceed its lifetime plus clock "
                      + "skew. The validator does not throw — it returns null; this is the package's "
                      + "deliberate contract.",
            NegativePath = true,
            Fields =
            [
                IssuerField,
                AudienceField,
                KeyField,
                new ScenarioField("lifetimeMs", "Lifetime (ms)", ScenarioFieldKind.Duration, "60000"),
                new ScenarioField("clockSkewMs", "ClockSkew (ms)", ScenarioFieldKind.Duration, "0"),
            ],
        },
        async context =>
        {
            var lifetime = context.Input.Duration("lifetimeMs");
            var skew = context.Input.Duration("clockSkewMs");

            await using var host = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = context.Input.Text("issuer");
                jwt.Audience = context.Input.Text("audience");
                jwt.SymmetricKey = context.Input.Text("symmetricKey");
                jwt.Lifetime = lifetime;
                jwt.ClockSkew = skew;
            }));

            var issuedAt = DateTimeOffset.UtcNow - lifetime - skew - TimeSpan.FromMinutes(1);
            var token = host.GetRequiredService<IJwtTokenGenerator>()
                .GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-expired")], issuedAt);

            context.Step("Back-dated token issued", $"issuedAt = {issuedAt:O}");
            context.Artifact("decoded token", Presentation.Jwt(token));

            var principal = await host.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);

            context.Require("Validation returned null (not an exception)", principal is null);
        });

    private static Scenario WrongAudience() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.wrong-audience",
            PackageId = Package,
            Title = "A wrong audience is rejected",
            Summary = "The token is issued in one container and validated in a second container that "
                      + "expects a different audience. Using two separate DI containers also proves "
                      + "the options cache doesn't leak between runs.",
            NegativePath = true,
            Fields =
            [
                KeyField,
                new ScenarioField("issuedAudience", "Issued audience", ScenarioFieldKind.Text, "app-a"),
                new ScenarioField("expectedAudience", "Expected audience", ScenarioFieldKind.Text, "app-b"),
            ],
        },
        async context =>
        {
            var key = context.Input.Text("symmetricKey");

            await using var issuer = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = "pinqponq";
                jwt.Audience = context.Input.Text("issuedAudience");
                jwt.SymmetricKey = key;
            }));

            var token = issuer.GetRequiredService<IJwtTokenGenerator>()
                .GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-aud")]);
            context.Step($"Token issued for '{context.Input.Text("issuedAudience")}'");

            await using var verifier = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = "pinqponq";
                jwt.Audience = context.Input.Text("expectedAudience");
                jwt.SymmetricKey = key;
            }));

            var principal = await verifier.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);

            context.Require(
                $"Validator expecting '{context.Input.Text("expectedAudience")}' rejected it",
                principal is null);
        });

    private static Scenario ShortKeyRejected() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.short-key",
            PackageId = Package,
            Title = "A key shorter than 32 bytes is rejected",
            Summary = "HMAC-SHA256 requires at least a 256-bit key. JwtOptionsValidator rejects it "
                      + "with a clear error as soon as the options are read.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("shortKey", "Short key", ScenarioFieldKind.Password, "too-short-key"),
            ],
        },
        async context =>
        {
            var thrown = await RecordAsync(async () =>
            {
                await using var host = context.Host(services => services.AddPinqponqIdentity(jwt =>
                {
                    jwt.Issuer = "pinqponq";
                    jwt.Audience = "clients";
                    jwt.SymmetricKey = context.Input.Text("shortKey");
                }));

                context.Step("Container set up with the short key, reading options");
                _ = host.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value;
            });

            context.Require("An exception was thrown", thrown is not null);
            context.Artifact(
                "exception",
                new { type = thrown!.GetType().FullName, message = thrown.Message });
            context.Check(
                "The error message explains the key length",
                thrown.ToString().Contains("32", StringComparison.Ordinal),
                thrown.Message);
        });

    private static Scenario JtiIssuedAndRevoked() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.revoke",
            PackageId = Package,
            Title = "jti generation and access token revocation",
            Summary = "The issued token has a jti claim. Once revoked via IAccessTokenRevocationStore + "
                      + "IAccessTokenRevocationService, ValidateAsync returns null.",
            Fields = [IssuerField, AudienceField, KeyField],
        },
        async context =>
        {
            var store = new InMemoryAccessTokenRevocationStore();

            await using var host = context.Host(services =>
            {
                services.AddPinqponqIdentity(jwt =>
                {
                    jwt.Issuer = context.Input.Text("issuer");
                    jwt.Audience = context.Input.Text("audience");
                    jwt.SymmetricKey = context.Input.Text("symmetricKey");
                });
                services.AddSingleton<IAccessTokenRevocationStore>(store);
            });

            var token = host.GetRequiredService<IJwtTokenGenerator>()
                .GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-revoke")]);
            context.Artifact("decoded token", Presentation.Jwt(token));

            var before = await host.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);
            context.Require("Valid before revocation", before is not null);

            var jti = before!.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                      ?? before.FindFirst("jti")?.Value;
            context.Require("jti claim is present", !string.IsNullOrEmpty(jti), jti);

            await host.GetRequiredService<IAccessTokenRevocationService>()
                .RevokeAccessTokenAsync(token, context.CancellationToken);
            context.Step("Access token jti revoked");

            var after = await host.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);
            context.Require("Null after revocation", after is null);
            context.Require("jti is present in the store", store.RevokedJtis.Contains(jti!), string.Join(", ", store.RevokedJtis));
        });

    private static Scenario PasswordHashing() => new(
        new ScenarioDescriptor
        {
            Id = "identity.password",
            PackageId = Package,
            Title = "Hash and verify a password",
            Summary = "Generates a PBKDF2 hash, verifies the correct and the wrong password. Two "
                      + "hashes of the same password coming out different shows that salting works.",
            Fields =
            [
                new ScenarioField("password", "Password", ScenarioFieldKind.Password, "Str0ng-Pass!"),
                new ScenarioField("wrongPassword", "Wrong password", ScenarioFieldKind.Password, "Str0ng-Pass?"),
            ],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = "pinqponq";
                jwt.Audience = "clients";
                jwt.SymmetricKey = DemoKey;
            }));

            var hasher = host.GetRequiredService<IPasswordHasher>();
            var password = context.Input.Text("password");

            var first = hasher.Hash(password);
            var second = hasher.Hash(password);
            context.Step("The same password hashed twice");
            context.Require("The two hashes differ (salted)", !string.Equals(first, second, StringComparison.Ordinal));
            context.Artifact("hash #1", first, "text");
            context.Artifact("hash #2", second, "text");

            var correct = hasher.Verify(first, password);
            var wrong = hasher.Verify(first, context.Input.Text("wrongPassword"));

            context.Require("Correct password accepted", correct != PasswordVerificationOutcome.Failed, correct.ToString());
            context.Require("Wrong password rejected", wrong == PasswordVerificationOutcome.Failed, wrong.ToString());
            context.Artifact("results", new { correct = correct.ToString(), wrong = wrong.ToString() });
        });

    private static Scenario RefreshTokenRotation() => new(
        new ScenarioDescriptor
        {
            Id = "identity.refresh.rotate",
            PackageId = Package,
            Title = "Issue and rotate a refresh token",
            Summary = "Issues a token, rotates it, and shows the store's contents: only a SHA-256 "
                      + "hash is kept, the old record is revoked and chained to the new one.",
            Fields =
            [
                new ScenarioField("subject", "Subject", ScenarioFieldKind.Text, "user-42"),
                new ScenarioField("lifetimeMs", "RefreshToken Lifetime (ms)", ScenarioFieldKind.Duration, "1209600000"),
            ],
        },
        async context =>
        {
            var store = new InMemoryRefreshTokenStore();

            await using var host = context.Host(services =>
            {
                services.AddPinqponqIdentity(
                    jwt =>
                    {
                        jwt.Issuer = "pinqponq";
                        jwt.Audience = "clients";
                        jwt.SymmetricKey = DemoKey;
                    },
                    refresh => refresh.Lifetime = context.Input.Duration("lifetimeMs"));

                services.AddSingleton<IRefreshTokenStore>(store);
            });

            var service = host.GetRequiredService<IRefreshTokenService>();
            var issued = await service.IssueAsync(context.Input.Text("subject"), context.CancellationToken);
            context.Step("Token issued");

            context.Require(
                "The raw token is not stored",
                store.All.All(token => !string.Equals(token.TokenHash, issued.Token, StringComparison.Ordinal)));
            context.Check("Stored value is 64-character hex", issued.Descriptor.TokenHash.Length == 64);

            var rotated = await service.RotateAsync(issued.Token, context.CancellationToken);
            context.Step("Token rotated");

            var old = await store.FindByHashAsync(issued.Descriptor.TokenHash, context.CancellationToken);
            context.Require("Old token revoked", old?.RevokedAt is not null);
            context.Require(
                "Old record chained to the new one",
                old?.ReplacedByTokenHash == rotated.Descriptor.TokenHash);

            context.Artifact("store contents", store.All.Select(token => new
            {
                tokenHash = token.TokenHash,
                subject = token.Subject,
                expiresAt = token.ExpiresAt,
                revokedAt = token.RevokedAt,
                replacedBy = token.ReplacedByTokenHash,
            }).ToArray());
        });

    private static Scenario RefreshTokenReuseDetected() => new(
        new ScenarioDescriptor
        {
            Id = "identity.refresh.reuse",
            PackageId = Package,
            Title = "A used refresh token (within grace)",
            Summary = "The same raw token is rotated twice. Within ReuseDetectionGrace, the second "
                      + "attempt throws InvalidRefreshTokenException but does not trigger a family "
                      + "revoke — so as not to punish a concurrent double-submit.",
            NegativePath = true,
        },
        async context =>
        {
            var store = new InMemoryRefreshTokenStore();

            await using var host = context.Host(services =>
            {
                services.AddPinqponqIdentity(
                    jwt =>
                    {
                        jwt.Issuer = "pinqponq";
                        jwt.Audience = "clients";
                        jwt.SymmetricKey = DemoKey;
                    },
                    refresh => refresh.ReuseDetectionGrace = TimeSpan.FromSeconds(30));
                services.AddSingleton<IRefreshTokenStore>(store);
            });

            var service = host.GetRequiredService<IRefreshTokenService>();
            var issued = await service.IssueAsync("user-42", context.CancellationToken);
            var rotated = await service.RotateAsync(issued.Token, context.CancellationToken);
            context.Step("Token rotated once");

            Exception? thrown = null;
            try
            {
                await service.RotateAsync(issued.Token, context.CancellationToken);
            }
            catch (InvalidRefreshTokenException exception)
            {
                thrown = exception;
            }

            context.Require("Second use rejected", thrown is not null);
            var replacement = await store.FindByHashAsync(rotated.Descriptor.TokenHash, context.CancellationToken);
            context.Require(
                "Replacement still active within grace",
                replacement is not null && replacement.RevokedAt is null);
            context.Artifact("exception", new { type = thrown!.GetType().FullName, message = thrown.Message });
        });

    private static Scenario RefreshTokenFamilyRevoke() => new(
        new ScenarioDescriptor
        {
            Id = "identity.refresh.family-revoke",
            PackageId = Package,
            Title = "Family revoke after ReuseDetectionGrace",
            Summary = "When the grace period is zero and an old token is reused, RevokeAllForSubjectAsync "
                      + "revokes the entire subject family — the stolen refresh token path.",
            NegativePath = true,
        },
        async context =>
        {
            var store = new InMemoryRefreshTokenStore();

            await using var host = context.Host(services =>
            {
                services.AddPinqponqIdentity(
                    jwt =>
                    {
                        jwt.Issuer = "pinqponq";
                        jwt.Audience = "clients";
                        jwt.SymmetricKey = DemoKey;
                    },
                    refresh => refresh.ReuseDetectionGrace = TimeSpan.Zero);
                services.AddSingleton<IRefreshTokenStore>(store);
            });

            var service = host.GetRequiredService<IRefreshTokenService>();
            var issued = await service.IssueAsync("user-family", context.CancellationToken);
            var rotated = await service.RotateAsync(issued.Token, context.CancellationToken);
            context.Step("Rotation complete; old token will be reused outside the grace period");

            Exception? thrown = null;
            try
            {
                await service.RotateAsync(issued.Token, context.CancellationToken);
            }
            catch (InvalidRefreshTokenException exception)
            {
                thrown = exception;
            }

            context.Require("Reuse rejected", thrown is not null);
            var replacement = await store.FindByHashAsync(rotated.Descriptor.TokenHash, context.CancellationToken);
            context.Require("Replacement revoked by family revoke", replacement?.RevokedAt is not null);
            context.Artifact("store", store.All.Select(token => new
            {
                token.TokenHash,
                token.Subject,
                token.RevokedAt,
                token.ReplacedByTokenHash,
            }).ToArray());
        });

    private static Scenario MissingStoreDetected() => new(
        new ScenarioDescriptor
        {
            Id = "identity.di.missing-store",
            PackageId = Package,
            Title = "Fails without a registered IRefreshTokenStore",
            Summary = "The package deliberately doesn't register a store; the application is expected "
                      + "to supply its own persistence. Without a store, IRefreshTokenService cannot "
                      + "be resolved and the error names the missing registration directly.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = "pinqponq";
                jwt.Audience = "clients";
                jwt.SymmetricKey = DemoKey;
            }));

            context.Step("Container set up without registering IRefreshTokenStore");

            var thrown = await RecordAsync(() =>
            {
                _ = host.GetRequiredService<IRefreshTokenService>();
                return Task.CompletedTask;
            });

            context.Require("IRefreshTokenService could not be resolved", thrown is not null);
            context.Check(
                "The error points to IRefreshTokenStore",
                thrown!.ToString().Contains("IRefreshTokenStore", StringComparison.Ordinal));
            context.Artifact("exception", new { type = thrown.GetType().FullName, message = thrown.Message });

            // The same container works as soon as the application supplies its own storage.
            await using var complete = context.Host(services =>
            {
                services.AddPinqponqIdentity(jwt =>
                {
                    jwt.Issuer = "pinqponq";
                    jwt.Audience = "clients";
                    jwt.SymmetricKey = DemoKey;
                });
                services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
            });

            context.Check(
                "Resolves once the store is added",
                complete.GetRequiredService<IRefreshTokenService>() is not null);
        });

    private static async Task<Exception?> RecordAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
