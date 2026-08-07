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
        yield return RateLimited();
        yield return MissingStoreDetected();
        yield return MissingChannelSender();
    }

    private static Scenario SmsRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "otp.sms",
            PackageId = Package,
            Title = "OTP over SMS: generate, read, verify",
            Summary = "The code goes out over the SMS channel; the console reads it back from the "
                      + "fake NetGSM record and verifies it. Only an HMAC-SHA256(+pepper) hash is "
                      + "kept in the store; the key has the form otp:{SHA256(purpose|recipient)}.",
            Fields =
            [
                new ScenarioField("recipient", "Recipient (phone)", ScenarioFieldKind.Text, "+90 555 111 22 33"),
                new ScenarioField("codeLength", "CodeLength", ScenarioFieldKind.Number, "6"),
                new ScenarioField("smsTemplate", "SmsTemplate", ScenarioFieldKind.Text, "Your verification code: {0}"),
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
                ConfigureSms(services, context);
                services.AddPinqponqOtp(otp =>
                {
                    OtpPlayground.ApplyDefaults(otp);
                    otp.CodeLength = context.Input.Int("codeLength");
                    otp.SmsTemplate = context.Input.Text("smsTemplate");
                });
                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);
            context.Step("Code generated and sent over the SMS channel");

            context.Require("Fake NetGSM received a message", fake.Requests.Count == 1);
            var sent = fake.Requests[0].Message ?? string.Empty;
            context.Artifact("sent SMS", sent, "text");

            var code = DigitsPattern().Match(sent).Value;
            context.Require("Code extracted from the message", code.Length == context.Input.Int("codeLength"), code);

            context.Artifact("store record", store.All.Select(record => new
            {
                key = record.Key,
                codeHash = record.CodeHash,
                recipient = record.Recipient,
                expiresAt = record.ExpiresAt,
                attempts = record.Attempts,
            }).ToArray());

            context.Check(
                "The store holds a hash, not the raw code",
                store.All.All(record => !record.CodeHash.Contains(code, StringComparison.Ordinal)));
            context.Check(
                "Key is otp: + 64 hex (purpose/recipient are not plaintext)",
                store.Keys.All(static key =>
                    key.StartsWith("otp:", StringComparison.Ordinal)
                    && key.Length == 68
                    && key[4..].All(static c => Uri.IsHexDigit(c))),
                string.Join(", ", store.Keys));

            var status = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Verification succeeded", status == OtpVerifyStatus.Success, status.ToString());

            var replay = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Code is single-use", replay == OtpVerifyStatus.NotFound, replay.ToString());
        });

    private static Scenario EmailRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "otp.email",
            PackageId = Package,
            Title = "OTP over email: generate, read from inbox, verify",
            Summary = "The code goes out over the email channel, lands in the MailHog inbox, and the "
                      + "console reads it back from there and verifies it. No SMS registration needed — "
                      + "as of 0.2.1 the sender is optional per channel.",
            RequiredServices = [DevServiceIds.MailHog],
            Fields =
            [
                new ScenarioField("recipient", "Recipient (email)", ScenarioFieldKind.Text, "user@pinqponq.dev"),
                new ScenarioField("subjectTemplate", "EmailSubjectTemplate", ScenarioFieldKind.Text, "Your verification code: {0}"),
            ],
            TimeoutSeconds = 45,
        },
        async context =>
        {
            var mailhog = context.AppServices.GetRequiredService<MailHogClient>();
            await mailhog.ClearAsync(context.CancellationToken);
            context.Step("MailHog inbox cleared");

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

                services.AddPinqponqOtp(otp =>
                {
                    OtpPlayground.ApplyDefaults(otp);
                    otp.EmailSubjectTemplate = context.Input.Text("subjectTemplate");
                });

                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Auto, "login", context.CancellationToken);
            context.Step("Auto channel routed to email (recipient contains @)");

            var mail = await WaitForMailAsync(mailhog, context);
            context.Require("Mail landed in the inbox", mail is not null);
            context.Artifact("received mail", new { mail!.Subject, mail.From, mail.To, mail.Body });

            var code = DigitsPattern().Match($"{mail.Subject} {mail.Body}").Value;
            context.Require("Code extracted from the mail", code.Length >= 4, code);

            var status = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Verification succeeded", status == OtpVerifyStatus.Success, status.ToString());
        });

    private static Scenario WrongCodeAndLockout() => new(
        new ScenarioDescriptor
        {
            Id = "otp.wrong-code",
            PackageId = Package,
            Title = "Wrong code and attempt limit",
            Summary = "A wrong code returns Mismatch and increments the attempt counter. Once "
                      + "MaxAttempts is exceeded, TooManyAttempts is returned and the record is "
                      + "deleted — even the correct code no longer works.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("recipient", "Recipient", ScenarioFieldKind.Text, "+90 555 111 22 33"),
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
                ConfigureSms(services, context);
                services.AddPinqponqOtp(otp =>
                {
                    OtpPlayground.ApplyDefaults(otp);
                    otp.MaxAttempts = maxAttempts;
                });
                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);

            var realCode = DigitsPattern().Match(fake.Requests[0].Message ?? string.Empty).Value;
            var wrongCode = new string(realCode.Select(c => c == '0' ? '1' : '0').ToArray());
            context.Step("Code generated", $"real {realCode}, will try {wrongCode}");

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var status = await service.VerifyAsync(recipient, wrongCode, "login", context.CancellationToken);
                context.Require($"Wrong attempt #{attempt} is Mismatch", status == OtpVerifyStatus.Mismatch, status.ToString());
            }

            var lockedOut = await service.VerifyAsync(recipient, wrongCode, "login", context.CancellationToken);
            context.Require("TooManyAttempts once the limit is exceeded", lockedOut == OtpVerifyStatus.TooManyAttempts, lockedOut.ToString());

            var afterLockout = await service.VerifyAsync(recipient, realCode, "login", context.CancellationToken);
            context.Require("Correct code no longer works since the record was deleted", afterLockout == OtpVerifyStatus.NotFound, afterLockout.ToString());

            context.Artifact("statuses", new
            {
                wrongAttempt = OtpVerifyStatus.Mismatch.ToString(),
                lockedOut = lockedOut.ToString(),
                afterLockout = afterLockout.ToString(),
            });
        });

    private static Scenario Expiry() => new(
        new ScenarioDescriptor
        {
            Id = "otp.expired",
            PackageId = Package,
            Title = "An expired code is rejected",
            Summary = "A code is generated with a short TTL, and verification is attempted after it "
                      + "expires. Expired is returned and the record is removed from the store.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("recipient", "Recipient", ScenarioFieldKind.Text, "+90 555 111 22 33"),
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
                ConfigureSms(services, context);
                services.AddPinqponqOtp(otp =>
                {
                    OtpPlayground.ApplyDefaults(otp);
                    otp.Ttl = ttl;
                });
                services.AddSingleton<IOtpStore>(store);
            });

            var service = host.GetRequiredService<IOtpService>();
            await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);
            var code = DigitsPattern().Match(fake.Requests[0].Message ?? string.Empty).Value;
            context.Step($"Code generated, TTL {ttl.TotalMilliseconds:0} ms");

            await Task.Delay(ttl + TimeSpan.FromMilliseconds(250), context.CancellationToken);
            context.Step("TTL expired");

            var status = await service.VerifyAsync(recipient, code, "login", context.CancellationToken);
            context.Require("Expired returned", status == OtpVerifyStatus.Expired, status.ToString());
            context.Require("Record removed from the store", store.All.Count == 0);
        });

    private static Scenario RateLimited() => new(
        new ScenarioDescriptor
        {
            Id = "otp.rate-limit",
            PackageId = Package,
            Title = "Send interval (MinSendInterval)",
            Summary = "With a real IOtpSendRateLimiter registered, a second send to the same "
                      + "recipient within MinSendInterval throws OtpSendRateLimitedException. The "
                      + "default AllowAllOtpSendRateLimiter hides this.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("recipient", "Recipient", ScenarioFieldKind.Text, "+90 555 111 22 33"),
                new ScenarioField("minIntervalMs", "MinSendInterval (ms)", ScenarioFieldKind.Duration, "60000"),
            ],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            await using var host = context.Host(services =>
            {
                ConfigureSms(services, context);
                services.AddPinqponqOtp(otp =>
                {
                    OtpPlayground.ApplyDefaults(otp);
                    otp.MinSendInterval = context.Input.Duration("minIntervalMs");
                });
                services.AddSingleton<IOtpStore, InMemoryOtpStore>();
                services.AddSingleton<IOtpSendRateLimiter, InMemoryOtpSendRateLimiter>();
            });

            var service = host.GetRequiredService<IOtpService>();
            var recipient = context.Input.Text("recipient");

            await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);
            context.Step("First send succeeded");

            Exception? thrown = null;
            try
            {
                await service.GenerateAndSendAsync(recipient, OtpChannel.Sms, "login", context.CancellationToken);
            }
            catch (OtpSendRateLimitedException exception)
            {
                thrown = exception;
            }

            context.Require("Second send throws OtpSendRateLimitedException", thrown is not null);
            context.Require("The fake endpoint received only one request", fake.Requests.Count == 1, $"{fake.Requests.Count}");
            context.Artifact("exception", new { type = thrown!.GetType().FullName, message = thrown.Message });
        });

    private static Scenario MissingStoreDetected() => new(
        new ScenarioDescriptor
        {
            Id = "otp.di.missing-store",
            PackageId = Package,
            Title = "Fails without a registered IOtpStore",
            Summary = "As of 0.2.1, AddPinqponqOtp does not break host startup; a missing IOtpStore "
                      + "is reported by name the first time IOtpService is resolved.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services =>
            {
                ConfigureSms(services, context);
                services.AddPinqponqOtp(OtpPlayground.ApplyDefaults);
            });

            context.Step("Container set up without registering IOtpStore");

            Exception? thrown = null;
            try
            {
                _ = host.GetRequiredService<IOtpService>();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            context.Require("IOtpService could not be resolved", thrown is not null);
            context.Check(
                "The error points to IOtpStore",
                thrown!.ToString().Contains("IOtpStore", StringComparison.Ordinal),
                thrown.Message);
            context.Artifact("exception", new { type = thrown.GetType().FullName, message = thrown.Message });
        });

    private static Scenario MissingChannelSender() => new(
        new ScenarioDescriptor
        {
            Id = "otp.missing-channel",
            PackageId = Package,
            Title = "A clear error when the channel sender is missing",
            Summary = "When sending over the SMS channel without an ISmsSender registered, the error "
                      + "names AddPinqponqSms — email-only applications no longer have to keep a "
                      + "mandatory SMS registration.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services =>
            {
                services.AddPinqponqOtp(OtpPlayground.ApplyDefaults);
                services.AddSingleton<IOtpStore, InMemoryOtpStore>();
            });

            Exception? thrown = null;
            try
            {
                await host.GetRequiredService<IOtpService>()
                    .GenerateAndSendAsync("+905551112233", OtpChannel.Sms, "login", context.CancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            context.Require("InvalidOperationException thrown", thrown is not null);
            context.Check(
                "The error names AddPinqponqSms",
                thrown!.Message.Contains("AddPinqponqSms", StringComparison.Ordinal),
                thrown.Message);
            context.Artifact("exception", new { type = thrown.GetType().FullName, message = thrown.Message });
        });

    private static void ConfigureSms(IServiceCollection services, ScenarioContext context)
    {
        services.AddPinqponqSms(sms =>
        {
            sms.ApiUrl = context.FakeSmsUrl;
            sms.UserCode = "playground";
            sms.Password = "playground-secret";
        });
        SmsSupport.TagOutgoingRequests(services, context.RunId);
    }

    private static async Task<CapturedMail?> WaitForMailAsync(MailHogClient mailhog, ScenarioContext context)
    {
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
