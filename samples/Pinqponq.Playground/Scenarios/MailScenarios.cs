using Microsoft.Extensions.Configuration;
using Pinqponq.Mail;
using Pinqponq.Mail.DependencyInjection;
using Pinqponq.Playground.Infrastructure;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Scenarios for <c>Pinqponq.Mail</c>, delivered into the MailHog container.</summary>
public static class MailScenarios
{
    private const string Package = "Pinqponq.Mail";

    public static IEnumerable<Scenario> Create()
    {
        yield return SendHtml();
        yield return MultipleRecipients();
        yield return ConfigurationBinding();
    }

    private static Scenario SendHtml() => new(
        new ScenarioDescriptor
        {
            Id = "mail.send",
            PackageId = Package,
            Title = "HTML mail gönder",
            Summary = "SMTP üzerinden mail gönderir ve MailHog kutusundan geri okuyup gösterir. "
                      + "Var olmayan bir ek dosyanın sessizce atlandığını da doğrular.",
            RequiredServices = [DevServiceIds.MailHog],
            Fields =
            [
                new ScenarioField("to", "Alıcı", ScenarioFieldKind.Text, "user@pinqponq.dev"),
                new ScenarioField("subject", "Konu", ScenarioFieldKind.Text, "Pinqponq Playground testi"),
                new ScenarioField("body", "Gövde (HTML)", ScenarioFieldKind.MultilineText,
                    "<h1>Merhaba</h1><p>Bu mail <strong>Pinqponq.Mail</strong> ile gönderildi.</p>"),
                new ScenarioField("attachment", "Ek dosya yolu (yok sayılmalı)", ScenarioFieldKind.Text,
                    "/tmp/olmayan-dosya.pdf", "Var olmayan ekler sessizce atlanır."),
            ],
            TimeoutSeconds = 45,
        },
        async context =>
        {
            var mailhog = context.AppServices.GetRequiredService<MailHogClient>();
            await mailhog.ClearAsync(context.CancellationToken);

            var endpoint = context.Stack.Require(DevServiceIds.MailHog);

            await using var host = context.Host(services => services.AddPinqponqMail(smtp =>
            {
                smtp.SmtpHost = endpoint.Host!;
                smtp.SmtpPort = endpoint.Port!.Value;
                smtp.EnableSsl = false;
                smtp.FromEmail = "playground@pinqponq.dev";
                smtp.FromName = "Pinqponq Playground";
            }));

            await host.GetRequiredService<IEmailSender>().SendAsync(
                new EmailMessage
                {
                    To = context.Input.Text("to"),
                    Subject = context.Input.Text("subject"),
                    Body = context.Input.Text("body"),
                    IsBodyHtml = true,
                    Attachments = [context.Input.Text("attachment")],
                },
                context.CancellationToken);

            context.Step("SendAsync tamamlandı (eksik ek hata vermedi)");

            var mail = await WaitForMailAsync(mailhog, context);
            context.Require("Mail MailHog kutusuna düştü", mail is not null);
            context.Check("Konu korunmuş", mail!.Subject == context.Input.Text("subject"), mail.Subject);
            context.Check(
                "Alıcı doğru",
                mail.To.Any(recipient => recipient.Contains(context.Input.Text("to"), StringComparison.OrdinalIgnoreCase)),
                string.Join(", ", mail.To));

            context.Artifact("gelen mail", new { mail.Subject, mail.From, mail.To, mail.Body, mail.ReceivedAt });
        });

    private static Scenario MultipleRecipients() => new(
        new ScenarioDescriptor
        {
            Id = "mail.recipients",
            PackageId = Package,
            Title = "Çoklu alıcı: virgül ve noktalı virgül",
            Summary = "To/Cc/Bcc alanları hem virgül hem noktalı virgülle ayrılmış listeleri "
                      + "kabul eder. Bcc'nin başlıklarda görünmediği de doğrulanır.",
            RequiredServices = [DevServiceIds.MailHog],
            Fields =
            [
                new ScenarioField("to", "To", ScenarioFieldKind.Text, "a@pinqponq.dev, b@pinqponq.dev"),
                new ScenarioField("cc", "Cc", ScenarioFieldKind.Text, "c@pinqponq.dev; d@pinqponq.dev"),
                new ScenarioField("bcc", "Bcc", ScenarioFieldKind.Text, "gizli@pinqponq.dev"),
            ],
            TimeoutSeconds = 45,
        },
        async context =>
        {
            var mailhog = context.AppServices.GetRequiredService<MailHogClient>();
            await mailhog.ClearAsync(context.CancellationToken);

            var endpoint = context.Stack.Require(DevServiceIds.MailHog);

            await using var host = context.Host(services => services.AddPinqponqMail(smtp =>
            {
                smtp.SmtpHost = endpoint.Host!;
                smtp.SmtpPort = endpoint.Port!.Value;
                smtp.EnableSsl = false;
                smtp.FromEmail = "playground@pinqponq.dev";
            }));

            await host.GetRequiredService<IEmailSender>().SendAsync(
                new EmailMessage
                {
                    To = context.Input.Text("to"),
                    Cc = context.Input.Text("cc"),
                    Bcc = context.Input.Text("bcc"),
                    Subject = "Çoklu alıcı testi",
                    Body = "<p>Ayraç testi</p>",
                },
                context.CancellationToken);

            context.Step("Mail gönderildi");

            var messages = await WaitForCountAsync(mailhog, context, expected: 5);
            context.Artifact("teslim edilen kopyalar", messages.Select(message => new
            {
                message.Subject,
                to = message.To,
            }).ToArray(), "table");

            context.Require(
                "Beş alıcının hepsine teslim edildi",
                messages.Count == 5,
                $"{messages.Count} kopya");

            var headerRecipients = messages
                .SelectMany(message => message.To)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            context.Check(
                "Bcc alıcısı başlıklarda görünmüyor",
                !headerRecipients.Any(recipient =>
                    recipient.Contains("gizli@", StringComparison.OrdinalIgnoreCase)),
                string.Join(", ", headerRecipients));
        });

    private static Scenario ConfigurationBinding() => new(
        new ScenarioDescriptor
        {
            Id = "mail.configuration",
            PackageId = Package,
            Title = "IConfiguration bölümünden bağlama",
            Summary = "AddPinqponqMail(IConfiguration, \"Smtp\") aşırı yüklemesi bölümü bağlar; "
                      + "bölüm yoksa anlaşılır bir InvalidOperationException verir. Docker gerekmez.",
        },
        context =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Smtp:SmtpHost"] = "smtp.pinqponq.dev",
                    ["Smtp:SmtpPort"] = "587",
                    ["Smtp:FromEmail"] = "no-reply@pinqponq.dev",
                    ["Smtp:EnableSsl"] = "true",
                })
                .Build();

            using var bound = new ServiceCollection()
                .AddPinqponqMail(configuration, "Smtp")
                .BuildServiceProvider();

            var options = bound.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmtpOptions>>().Value;
            context.Require("SmtpHost bağlandı", options.SmtpHost == "smtp.pinqponq.dev", options.SmtpHost);
            context.Require("SmtpPort bağlandı", options.SmtpPort == 587, options.SmtpPort.ToString());
            context.Artifact("bağlanan options", options);

            Exception? thrown = null;
            try
            {
                new ServiceCollection().AddPinqponqMail(configuration, "OlmayanBolum");
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            context.Require("Eksik bölüm hata verdi", thrown is not null);
            context.Check(
                "Hata bölüm adını söylüyor",
                thrown!.Message.Contains("OlmayanBolum", StringComparison.Ordinal),
                thrown.Message);

            return Task.CompletedTask;
        });

    private static async Task<CapturedMail?> WaitForMailAsync(MailHogClient mailhog, ScenarioContext context)
    {
        var messages = await WaitForCountAsync(mailhog, context, expected: 1);
        return messages.Count > 0 ? messages[0] : null;
    }

    private static async Task<IReadOnlyList<CapturedMail>> WaitForCountAsync(
        MailHogClient mailhog,
        ScenarioContext context,
        int expected)
    {
        IReadOnlyList<CapturedMail> messages = [];
        for (var attempt = 0; attempt < 25; attempt++)
        {
            messages = await mailhog.ListAsync(50, context.CancellationToken);
            if (messages.Count >= expected)
            {
                return messages;
            }

            await Task.Delay(200, context.CancellationToken);
        }

        return messages;
    }
}
