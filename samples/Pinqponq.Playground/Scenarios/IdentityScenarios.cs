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
            "HMAC için en az 32 bayt olmalı.");

    public static IEnumerable<Scenario> Create()
    {
        yield return HmacRoundTrip();
        yield return RsaRoundTrip();
        yield return ExpiredToken();
        yield return WrongAudience();
        yield return ShortKeyRejected();
        yield return PasswordHashing();
        yield return RefreshTokenRotation();
        yield return RefreshTokenReuseDetected();
        yield return MissingStoreDetected();
    }

    private static Scenario HmacRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.hmac",
            PackageId = Package,
            Title = "JWT üret ve doğrula (HMAC-SHA256)",
            Summary = "Claim listesiyle token üretir, aynı yapılandırmayla doğrular ve çözülmüş "
                      + "header/payload ile dönen ClaimsPrincipal'ı gösterir.",
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

            context.Step("AddPinqponqIdentity ile izole konteyner kuruldu");

            var generator = host.GetRequiredService<IJwtTokenGenerator>();
            var token = generator.GenerateToken(
            [
                new Claim(ClaimTypes.NameIdentifier, context.Input.Text("subject")),
                new Claim(ClaimTypes.Email, context.Input.Text("email")),
                new Claim(ClaimTypes.Role, context.Input.Text("role")),
            ]);

            context.Step("Token üretildi", $"{token.Length} karakter");
            context.Artifact("token", token, "token");
            context.Artifact("çözülmüş token", Presentation.Jwt(token));

            var validator = host.GetRequiredService<IJwtTokenValidator>();
            var principal = await validator.ValidateAsync(token, context.CancellationToken);

            context.Require("Token doğrulandı", principal is not null);
            context.Artifact("ClaimsPrincipal", Presentation.Principal(principal!));

            context.Check(
                "sub claim korunmuş",
                principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value == context.Input.Text("subject"));
            context.Check(
                "role claim korunmuş",
                principal.FindFirst(ClaimTypes.Role)?.Value == context.Input.Text("role"));
        });

    private static Scenario RsaRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.rsa",
            PackageId = Package,
            Title = "JWT üret ve doğrula (RSA-SHA256)",
            Summary = "2048 bit RSA anahtar çifti üretir, özel anahtarla imzalar, açık anahtarla "
                      + "doğrular. Token'ın alg başlığının RS256 olduğunu gösterir.",
            Fields = [IssuerField, AudienceField],
        },
        async context =>
        {
            using var rsa = RSA.Create(2048);
            var privatePem = rsa.ExportPkcs8PrivateKeyPem();
            var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
            context.Step("RSA-2048 anahtar çifti üretildi");

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
            context.Artifact("çözülmüş token", Presentation.Jwt(token));
            context.Artifact("açık anahtar (PEM)", publicPem, "text");

            var principal = await host.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);

            context.Require("Açık anahtarla doğrulandı", principal is not null);
        });

    private static Scenario ExpiredToken() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.expired",
            PackageId = Package,
            Title = "Süresi dolmuş token reddedilir",
            Summary = "Token, ömrü ve saat toleransı kadar geçmişte üretilir. Doğrulayıcı "
                      + "exception fırlatmaz — null döner; bu paketin bilinçli sözleşmesidir.",
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

            context.Step("Geçmiş tarihli token üretildi", $"issuedAt = {issuedAt:O}");
            context.Artifact("çözülmüş token", Presentation.Jwt(token));

            var principal = await host.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);

            context.Require("Doğrulama null döndü (exception değil)", principal is null);
        });

    private static Scenario WrongAudience() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.wrong-audience",
            PackageId = Package,
            Title = "Yanlış audience reddedilir",
            Summary = "Token bir konteynerde üretilir, farklı bir audience bekleyen ikinci bir "
                      + "konteynerde doğrulanır. İki ayrı DI konteyneri kullanmak, options "
                      + "önbelleğinin koşular arasında sızmadığını da kanıtlar.",
            NegativePath = true,
            Fields =
            [
                KeyField,
                new ScenarioField("issuedAudience", "Üretilen audience", ScenarioFieldKind.Text, "app-a"),
                new ScenarioField("expectedAudience", "Beklenen audience", ScenarioFieldKind.Text, "app-b"),
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
            context.Step($"Token '{context.Input.Text("issuedAudience")}' için üretildi");

            await using var verifier = context.Host(services => services.AddPinqponqIdentity(jwt =>
            {
                jwt.Issuer = "pinqponq";
                jwt.Audience = context.Input.Text("expectedAudience");
                jwt.SymmetricKey = key;
            }));

            var principal = await verifier.GetRequiredService<IJwtTokenValidator>()
                .ValidateAsync(token, context.CancellationToken);

            context.Require(
                $"'{context.Input.Text("expectedAudience")}' bekleyen doğrulayıcı reddetti",
                principal is null);
        });

    private static Scenario ShortKeyRejected() => new(
        new ScenarioDescriptor
        {
            Id = "identity.jwt.short-key",
            PackageId = Package,
            Title = "32 bayttan kısa anahtar reddedilir",
            Summary = "HMAC-SHA256 en az 256 bit anahtar ister. Paket bunu sessizce kabul etmek "
                      + "yerine anlaşılır bir hatayla reddeder. Kontrol, kayıt anında değil "
                      + "imzalama anahtarı ilk kez kurulduğunda — yani ilk token üretiminde — çalışır.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("shortKey", "Kısa anahtar", ScenarioFieldKind.Password, "cok-kisa-anahtar"),
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

                var generator = host.GetRequiredService<IJwtTokenGenerator>();
                context.Step("Kısa anahtarla konteyner kuruldu, token üretimi deneniyor");
                _ = generator.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "user-short-key")]);
            });

            context.Require("Bir exception fırlatıldı", thrown is not null);
            context.Artifact(
                "exception",
                new { type = thrown!.GetType().FullName, message = thrown.Message });
            context.Check(
                "Hata mesajı anahtar uzunluğunu açıklıyor",
                thrown.ToString().Contains("32", StringComparison.Ordinal),
                thrown.Message);
        });

    private static Scenario PasswordHashing() => new(
        new ScenarioDescriptor
        {
            Id = "identity.password",
            PackageId = Package,
            Title = "Parola hash'le ve doğrula",
            Summary = "PBKDF2 hash üretir, doğru ve yanlış parolayı doğrular. Aynı parolanın iki "
                      + "hash'inin farklı çıkması tuzlamanın çalıştığını gösterir.",
            Fields =
            [
                new ScenarioField("password", "Parola", ScenarioFieldKind.Password, "Str0ng-Pass!"),
                new ScenarioField("wrongPassword", "Yanlış parola", ScenarioFieldKind.Password, "Str0ng-Pass?"),
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
            context.Step("Aynı parola iki kez hash'lendi");
            context.Require("İki hash farklı (tuzlanmış)", !string.Equals(first, second, StringComparison.Ordinal));
            context.Artifact("hash #1", first, "text");
            context.Artifact("hash #2", second, "text");

            var correct = hasher.Verify(first, password);
            var wrong = hasher.Verify(first, context.Input.Text("wrongPassword"));

            context.Require("Doğru parola kabul edildi", correct != PasswordVerificationOutcome.Failed, correct.ToString());
            context.Require("Yanlış parola reddedildi", wrong == PasswordVerificationOutcome.Failed, wrong.ToString());
            context.Artifact("sonuçlar", new { dogru = correct.ToString(), yanlis = wrong.ToString() });
        });

    private static Scenario RefreshTokenRotation() => new(
        new ScenarioDescriptor
        {
            Id = "identity.refresh.rotate",
            PackageId = Package,
            Title = "Refresh token üret ve döndür (rotate)",
            Summary = "Token üretir, döndürür ve deponun içeriğini gösterir: yalnızca SHA-256 "
                      + "hash saklanır, eski kayıt revoke edilip yenisine zincirlenir.",
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
            context.Step("Token üretildi");

            context.Require(
                "Ham token depoda saklanmıyor",
                store.All.All(token => !string.Equals(token.TokenHash, issued.Token, StringComparison.Ordinal)));
            context.Check("Saklanan değer 64 karakter hex", issued.Descriptor.TokenHash.Length == 64);

            var rotated = await service.RotateAsync(issued.Token, context.CancellationToken);
            context.Step("Token döndürüldü (rotate)");

            var old = await store.FindByHashAsync(issued.Descriptor.TokenHash, context.CancellationToken);
            context.Require("Eski token revoke edildi", old?.RevokedAt is not null);
            context.Require(
                "Eski kayıt yenisine zincirlendi",
                old?.ReplacedByTokenHash == rotated.Descriptor.TokenHash);

            context.Artifact("depo içeriği", store.All.Select(token => new
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
            Title = "Kullanılmış refresh token yeniden kullanılamaz",
            Summary = "Aynı ham token iki kez döndürülmeye çalışılır. İkinci deneme "
                      + "InvalidRefreshTokenException ile reddedilir — çalınmış token tespiti.",
            NegativePath = true,
        },
        async context =>
        {
            var store = new InMemoryRefreshTokenStore();

            await using var host = context.Host(services =>
            {
                services.AddPinqponqIdentity(jwt =>
                {
                    jwt.Issuer = "pinqponq";
                    jwt.Audience = "clients";
                    jwt.SymmetricKey = DemoKey;
                });
                services.AddSingleton<IRefreshTokenStore>(store);
            });

            var service = host.GetRequiredService<IRefreshTokenService>();
            var issued = await service.IssueAsync("user-42", context.CancellationToken);
            await service.RotateAsync(issued.Token, context.CancellationToken);
            context.Step("Token bir kez döndürüldü");

            Exception? thrown = null;
            try
            {
                await service.RotateAsync(issued.Token, context.CancellationToken);
            }
            catch (InvalidRefreshTokenException exception)
            {
                thrown = exception;
            }

            context.Require("İkinci kullanım reddedildi", thrown is not null);
            context.Artifact("exception", new { type = thrown!.GetType().FullName, message = thrown.Message });
        });

    private static Scenario MissingStoreDetected() => new(
        new ScenarioDescriptor
        {
            Id = "identity.di.missing-store",
            PackageId = Package,
            Title = "IRefreshTokenStore kaydedilmezse hata verir",
            Summary = "Paket depoyu bilinçli olarak kaydetmez; uygulamanın kendi kalıcılığını "
                      + "vermesi beklenir. Depo olmadan IRefreshTokenService çözülemez ve hata "
                      + "eksik kaydı doğrudan adıyla söyler.",
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

            context.Step("IRefreshTokenStore kaydedilmeden konteyner kuruldu");

            var thrown = await RecordAsync(() =>
            {
                _ = host.GetRequiredService<IRefreshTokenService>();
                return Task.CompletedTask;
            });

            context.Require("IRefreshTokenService çözülemedi", thrown is not null);
            context.Check(
                "Hata IRefreshTokenStore'u işaret ediyor",
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
                "Depo eklenince çözülüyor",
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
