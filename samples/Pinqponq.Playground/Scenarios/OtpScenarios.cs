using System.Text.RegularExpressions;
using Pinqponq.Identity.Otp;
using Pinqponq.Identity.Otp.DependencyInjection;
using Pinqponq.Mail.DependencyInjection;
using Pinqponq.Playground.Infrastructure;
using Pinqponq.Playground.Scenarios.Support;
using Pinqponq.Sms.DependencyInjection;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for <c>Pinqponq.Identity.Otp</c>.
/// </summary>
/// <remarks>
/// <c>GenerateAndSendAsync</c> deliberately never returns the code — it only leaves through
/// the delivery channel. So verifying it end to end means reading the code back out of the
/// fake SMS endpoint or the MailHog inbox, which exercises OTP, Sms and Mail together.
/// </remarks>
public static partial class OtpScenarios
{
    private const string Package = "Pinqponq.Identity.Otp";

    public static IEnumerable<Scenario> Create()
    {
        yield return SmsRoundTrip();
        yield return EmailRoundTrip();
        yield return WrongCodeAndLockout();
        yield return Expiry();
    }

    private static Scenario SmsRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "otp.sms",
            PackageId = Package,
            Title = "SMS ile OTP: üret, oku, doğrula",
            Summary = "Kod SMS kanalına gider; konsol sahte NetGSM kaydından kodu okur ve "
                      + "doğrular. Kodun yalnızca hash'inin saklandığı depoda görülebilir.",
            Fields =
            [
                new ScenarioField("recipient", "Alıcı (telefon)", ScenarioFieldKind.Text, "+90 555 111 22 33"),
                new ScenarioField("codeLength", "CodeLength", ScenarioFieldKind.Number, "6"),
                new ScenarioField("smsTemplate", "SmsTemplate", ScenarioFieldKind.Text, "Doğrulama kodunuz: {0}"),
            ],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            var store = new InMemoryOtpStore();
            var recipient = context.Input.Text("recipient");

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                });
                SmsSupport.TagOutgoingRequests(services, context.RunId);

                // The OTP service requires both channels even when only one is used.
                services.AddPinqponqMail(smtp => smtp.SmtpHost = "unused.invalid");

                services.AddPinqponqOtp(otp =>
                {
                    otp.CodeLength = context.Input.Int("codeLength");
                    otp.SmsTemplate = context.Input.Text("smsTemplate");
                });

                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);
            context.Step("Kod üretildi ve SMS kanalına gönderildi");

            context.Require("Sahte NetGSM bir mesaj aldı", fake.Requests.Count == 1);
            var sent = fake.Requests[0].Message ?? string.Empty;
            context.Artifact("gönderilen SMS", sent, "text");

            var code = DigitsPattern().Match(sent).Value;
            context.Require("Mesajdan kod ayıklandı", code.Length == context.Input.Int("codeLength"), code);

            context.Artifact("depo kaydı", store.All.Select(record => new
            {
                key = record.Key,
                codeHash = record.CodeHash,
                recipient = record.Recipient,
                expiresAt = record.ExpiresAt,
                attempts = record.Attempts,
            }).ToArray());

            context.Check(
                "Depoda ham kod değil hash var",
                store.All.All(record => !record.CodeHash.Contains(code, StringComparison.Ordinal)));
            context.Check(
                "Anahtar biçimi otp:{purpose}:{recipient}",
                store.Keys.Any(key => key.StartsWith("otp:login:", StringComparison.Ordinal)),
                string.Join(", ", store.Keys));

            var status = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Doğrulama başarılı", status == OtpVerifyStatus.Success, status.ToString());

            var replay = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Kod tek kullanımlık", replay == OtpVerifyStatus.NotFound, replay.ToString());
        });

    private static Scenario EmailRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "otp.email",
            PackageId = Package,
            Title = "E-posta ile OTP: üret, gelen kutusundan oku, doğrula",
            Summary = "Kod e-posta kanalına gider, MailHog kutusuna düşer, konsol kodu oradan "
                      + "okuyup doğrular. Otp + Mail paketlerini birlikte kanıtlar.",
            RequiredServices = [DevServiceIds.MailHog],
            Fields =
            [
                new ScenarioField("recipient", "Alıcı (e-posta)", ScenarioFieldKind.Text, "user@pinqponq.dev"),
                new ScenarioField("subjectTemplate", "EmailSubjectTemplate", ScenarioFieldKind.Text, "Doğrulama kodunuz: {0}"),
            ],
            TimeoutSeconds = 45,
        },
        async context =>
        {
            var mailhog = context.AppServices.GetRequiredService<MailHogClient>();
            await mailhog.ClearAsync(context.CancellationToken);
            context.Step("MailHog gelen kutusu temizlendi");

            var endpoint = context.Stack.Require(DevServiceIds.MailHog);
            var store = new InMemoryOtpStore();
            var recipient = context.Input.Text("recipient");

            await using var host = context.Host(services =>
            {
                services.AddPinqponqMail(smtp =>
                {
                    smtp.SmtpHost = endpoint.Host!;
                    smtp.SmtpPort = endpoint.Port!.Value;
                    smtp.EnableSsl = false;
                    smtp.FromEmail = "otp@pinqponq.dev";
                    smtp.FromName = "Pinqponq Playground";
                });

                services.AddPinqponqSms(sms => sms.ApiUrl = null);

                services.AddPinqponqOtp(otp =>
                    otp.EmailSubjectTemplate = context.Input.Text("subjectTemplate"));

                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Auto, "login", context.CancellationToken);
            context.Step("Auto kanal e-postaya yönlendi (alıcıda @ var)");

            var mail = await WaitForMailAsync(mailhog, context);
            context.Require("Mail gelen kutusuna düştü", mail is not null);
            context.Artifact("gelen mail", new { mail!.Subject, mail.From, mail.To, mail.Body });

            var code = DigitsPattern().Match($"{mail.Subject} {mail.Body}").Value;
            context.Require("Mailden kod ayıklandı", code.Length >= 4, code);

            var status = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Doğrulama başarılı", status == OtpVerifyStatus.Success, status.ToString());
        });

    private static Scenario WrongCodeAndLockout() => new(
        new ScenarioDescriptor
        {
            Id = "otp.wrong-code",
            PackageId = Package,
            Title = "Yanlış kod ve deneme sınırı",
            Summary = "Yanlış kod Mismatch döner ve deneme sayacını artırır. MaxAttempts "
                      + "aşılınca TooManyAttempts döner ve kayıt silinir — doğru kod bile artık geçmez.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("recipient", "Alıcı", ScenarioFieldKind.Text, "+90 555 111 22 33"),
                new ScenarioField("maxAttempts", "MaxAttempts", ScenarioFieldKind.Number, "2"),
            ],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            var store = new InMemoryOtpStore();
            var recipient = context.Input.Text("recipient");
            var maxAttempts = context.Input.Int("maxAttempts");

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                });
                SmsSupport.TagOutgoingRequests(services, context.RunId);
                services.AddPinqponqMail(smtp => smtp.SmtpHost = "unused.invalid");
                services.AddPinqponqOtp(otp => otp.MaxAttempts = maxAttempts);
                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);

            var realCode = DigitsPattern().Match(fake.Requests[0].Message ?? string.Empty).Value;
            var wrongCode = new string(realCode.Select(c => c == '0' ? '1' : '0').ToArray());
            context.Step("Kod üretildi", $"gerçek {realCode}, denenecek {wrongCode}");

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var status = await service.VerifyAsync(recipient, wrongCode, "login", context.CancellationToken);
                context.Require($"{attempt}. yanlış deneme Mismatch", status == OtpVerifyStatus.Mismatch, status.ToString());
            }

            var lockedOut = await service.VerifyAsync(recipient, wrongCode, "login", context.CancellationToken);
            context.Require("Sınır aşılınca TooManyAttempts", lockedOut == OtpVerifyStatus.TooManyAttempts, lockedOut.ToString());

            var afterLockout = await service.VerifyAsync(recipient, realCode, "login", context.CancellationToken);
            context.Require("Kayıt silindiği için doğru kod da geçmiyor", afterLockout == OtpVerifyStatus.NotFound, afterLockout.ToString());

            context.Artifact("durumlar", new
            {
                yanlisDeneme = OtpVerifyStatus.Mismatch.ToString(),
                sinirAsimi = lockedOut.ToString(),
                sonrasinda = afterLockout.ToString(),
            });
        });

    private static Scenario Expiry() => new(
        new ScenarioDescriptor
        {
            Id = "otp.expired",
            PackageId = Package,
            Title = "Süresi dolan kod reddedilir",
            Summary = "Kısa bir TTL ile kod üretilir, süresi dolduktan sonra doğrulanmaya "
                      + "çalışılır. Expired döner ve kayıt depodan silinir.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("recipient", "Alıcı", ScenarioFieldKind.Text, "+90 555 111 22 33"),
                new ScenarioField("ttlMs", "Ttl (ms)", ScenarioFieldKind.Duration, "1000"),
            ],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            var store = new InMemoryOtpStore();
            var recipient = context.Input.Text("recipient");
            var ttl = context.Input.Duration("ttlMs");

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                });
                SmsSupport.TagOutgoingRequests(services, context.RunId);
                services.AddPinqponqMail(smtp => smtp.SmtpHost = "unused.invalid");
                services.AddPinqponqOtp(otp => otp.Ttl = ttl);
                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);
            var code = DigitsPattern().Match(fake.Requests[0].Message ?? string.Empty).Value;
            context.Step($"Kod üretildi, TTL {ttl.TotalMilliseconds:0} ms");

            await Task.Delay(ttl + TimeSpan.FromMilliseconds(250), context.CancellationToken);
            context.Step("TTL doldu");

            var status = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Expired döndü", status == OtpVerifyStatus.Expired, status.ToString());
            context.Require("Kayıt depodan silindi", store.All.Count == 0);
        });

    private static async Task<CapturedMail?> WaitForMailAsync(MailHogClient mailhog, ScenarioContext context)
    {
        // SMTP delivery is asynchronous from the sender's point of view; MailHog needs a
        // moment to index the message.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var messages = await mailhog.ListAsync(10, context.CancellationToken);
            if (messages.Count > 0)
            {
                return messages[0];
            }

            await Task.Delay(200, context.CancellationToken);
        }

        return null;
    }

    [GeneratedRegex(@"\d{4,10}")]
    private static partial Regex DigitsPattern();
}
