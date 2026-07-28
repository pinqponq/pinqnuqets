using Pinqponq.Auth.Totp;
using Pinqponq.Auth.Totp.DependencyInjection;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Scenarios for <c>Pinqponq.Auth.Totp</c>. No infrastructure required.</summary>
public static class TotpScenarios
{
    private const string Package = "Pinqponq.Auth.Totp";

    private static readonly ScenarioField DigitsField =
        new("digits", "Digits", ScenarioFieldKind.Number, "6");

    private static readonly ScenarioField PeriodField =
        new("periodSeconds", "PeriodSeconds", ScenarioFieldKind.Number, "30");

    private static readonly ScenarioField AlgorithmField =
        new("algorithm", "Algorithm", ScenarioFieldKind.Enum, "Sha1", null, ["Sha1", "Sha256", "Sha512"]);

    public static IEnumerable<Scenario> Create()
    {
        yield return GenerateAndValidate();
        yield return DriftWindow();
        yield return WrongCode();
        yield return Base32RoundTrip();
    }

    private static Scenario GenerateAndValidate() => new(
        new ScenarioDescriptor
        {
            Id = "totp.generate-validate",
            PackageId = Package,
            Title = "Secret üret, kod hesapla, doğrula",
            Summary = "Yeni bir secret üretir, Authenticator uygulamalarının okuduğu otpauth:// "
                      + "URI'sini kurar, o andaki kodu hesaplar ve doğrular.",
            Fields =
            [
                new ScenarioField("account", "Hesap adı", ScenarioFieldKind.Text, "user@pinqponq.dev"),
                new ScenarioField("issuer", "Issuer", ScenarioFieldKind.Text, "Pinqponq"),
                DigitsField,
                PeriodField,
                AlgorithmField,
            ],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqTotp(totp =>
            {
                totp.Digits = context.Input.Int("digits");
                totp.PeriodSeconds = context.Input.Int("periodSeconds");
                totp.Algorithm = context.Input.Enum<TotpAlgorithm>("algorithm");
                totp.Issuer = context.Input.Text("issuer");
            }));

            var service = host.GetRequiredService<ITotpService>();

            var secret = service.GenerateSecret();
            context.Step("Secret üretildi", $"{secret.Length} karakter Base32");
            context.Artifact("secret", secret, "text");

            var uri = service.GetProvisioningUri(secret, context.Input.Text("account"));
            context.Artifact("otpauth URI", uri, "uri");
            context.Check("URI otpauth://totp/ ile başlıyor", uri.StartsWith("otpauth://totp/", StringComparison.Ordinal));

            var code = service.ComputeCode(secret);
            context.Step("Anlık kod hesaplandı", code);
            context.Artifact("kod", code, "text");

            context.Require("Kod doğrulandı", service.Validate(secret, code));
            context.Check(
                "Kod istenen uzunlukta",
                code.Length == context.Input.Int("digits"),
                $"{code.Length} basamak");
        });

    private static Scenario DriftWindow() => new(
        new ScenarioDescriptor
        {
            Id = "totp.drift-window",
            PackageId = Package,
            Title = "Saat kayması penceresi (ValidationWindow)",
            Summary = "Bir önceki periyodun kodu ValidationWindow=1 iken kabul edilir, 0 iken "
                      + "reddedilir. Kullanıcının saati birkaç saniye geriyse yaşanan durum.",
            Fields = [PeriodField],
        },
        async context =>
        {
            var period = context.Input.Int("periodSeconds");
            var now = DateTimeOffset.UtcNow;
            var previousPeriod = now.AddSeconds(-period);

            await using var tolerant = context.Host(services => services.AddPinqponqTotp(totp =>
            {
                totp.PeriodSeconds = period;
                totp.ValidationWindow = 1;
            }));

            var service = tolerant.GetRequiredService<ITotpService>();
            var secret = service.GenerateSecret();
            var oldCode = service.ComputeCode(secret, previousPeriod);
            context.Step("Bir önceki periyodun kodu hesaplandı", oldCode);

            context.Require("ValidationWindow=1 ile kabul edildi", service.Validate(secret, oldCode, now));

            await using var strict = context.Host(services => services.AddPinqponqTotp(totp =>
            {
                totp.PeriodSeconds = period;
                totp.ValidationWindow = 0;
            }));

            context.Require(
                "ValidationWindow=0 ile reddedildi",
                !strict.GetRequiredService<ITotpService>().Validate(secret, oldCode, now));
        });

    private static Scenario WrongCode() => new(
        new ScenarioDescriptor
        {
            Id = "totp.wrong-code",
            PackageId = Package,
            Title = "Yanlış kod reddedilir",
            Summary = "Rastgele bir kod ve boş bir kod denenir; ikisi de reddedilmeli.",
            NegativePath = true,
            Fields = [new ScenarioField("code", "Denenecek kod", ScenarioFieldKind.Text, "000000")],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqTotp());
            var service = host.GetRequiredService<ITotpService>();
            var secret = service.GenerateSecret();
            var real = service.ComputeCode(secret);
            var attempt = context.Input.Text("code");

            if (string.Equals(attempt, real, StringComparison.Ordinal))
            {
                // A one-in-a-million collision would otherwise look like a package defect.
                attempt = real == "000000" ? "111111" : "000000";
                context.Step("Denenen kod gerçek kodla çakıştı, değiştirildi", attempt);
            }

            context.Require("Yanlış kod reddedildi", !service.Validate(secret, attempt));
            context.Require("Boş kod reddedildi", !service.Validate(secret, string.Empty));
            context.Artifact("karşılaştırma", new { gercekKod = real, denenen = attempt });
        });

    private static Scenario Base32RoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "totp.base32",
            PackageId = Package,
            Title = "Base32 encode/decode",
            Summary = "Paketin dışa açtığı RFC 4648 Base32 yardımcısı: metin → Base32 → geri. "
                      + "Geçersiz karakterin FormatException verdiğini de gösterir.",
            Fields = [new ScenarioField("text", "Metin", ScenarioFieldKind.Text, "Pinqponq")],
        },
        context =>
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(context.Input.Text("text"));
            var encoded = Base32.Encode(bytes);
            context.Step("Encode edildi", encoded);

            var decoded = Base32.Decode(encoded);
            var roundTrip = System.Text.Encoding.UTF8.GetString(decoded);

            context.Require("Round-trip aynı metni verdi", roundTrip == context.Input.Text("text"), roundTrip);
            context.Artifact("sonuç", new { girdi = context.Input.Text("text"), base32 = encoded, cozulmus = roundTrip });

            Exception? thrown = null;
            try
            {
                Base32.Decode("!!!invalid!!!");
            }
            catch (FormatException exception)
            {
                thrown = exception;
            }

            context.Require("Geçersiz karakter FormatException verdi", thrown is not null);
            context.Artifact("exception", new { type = thrown!.GetType().Name, message = thrown.Message });

            return Task.CompletedTask;
        });
}
