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
        yield return AttachmentRootJail();
        yield return MultipleRecipients();
        yield return ConfigurationBinding();
    }

    private static Scenario SendHtml() => new(
        new ScenarioDescriptor
        {
            Id = "mail.send",
            PackageId = Package,
            Title = "Send an HTML mail",
            Summary = "Sends a mail over SMTP and reads it back from the MailHog inbox. No "
                      + "attachment — AttachmentRoot is only required when sending attachments.",
            RequiredServices = [DevServiceIds.MailHog],
            Fields =
            [
                new ScenarioField("to", "Recipient", ScenarioFieldKind.Text, "user@pinqponq.dev"),
                new ScenarioField("subject", "Subject", ScenarioFieldKind.Text, "Pinqponq Playground test"),
                new ScenarioField("body", "Body (HTML)", ScenarioFieldKind.MultilineText,
                    "<h1>Hello</h1><p>This mail was sent with <strong>Pinqponq.Mail</strong>.</p>"),
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
                },
                context.CancellationToken);

            context.Step("SendAsync completed");

            var mail = await WaitForMailAsync(mailhog, context);
            context.Require("Mail landed in the MailHog inbox", mail is not null);
            context.Check("Subject preserved", mail!.Subject == context.Input.Text("subject"), mail.Subject);
            context.Check(
                "Recipient is correct",
                mail.To.Any(recipient => recipient.Contains(context.Input.Text("to"), StringComparison.OrdinalIgnoreCase)),
                string.Join(", ", mail.To));

            context.Artifact("received mail", new { mail.Subject, mail.From, mail.To, mail.Body, mail.ReceivedAt });
        });

    private static Scenario AttachmentRootJail() => new(
        new ScenarioDescriptor
        {
            Id = "mail.attachment-root",
            PackageId = Package,
            Title = "AttachmentRoot path jail",
            Summary = "AttachmentRoot is required when sending attachments. A file under the root "
                      + "is attached; a missing file throws ArgumentException (no silent skip).",
            RequiredServices = [DevServiceIds.MailHog],
            NegativePath = true,
            TimeoutSeconds = 45,
        },
        async context =>
        {
            var mailhog = context.AppServices.GetRequiredService<MailHogClient>();
            await mailhog.ClearAsync(context.CancellationToken);
            var endpoint = context.Stack.Require(DevServiceIds.MailHog);

            var root = Path.Combine(Path.GetTempPath(), "pinqponq-playground-mail", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var attachmentPath = Path.Combine(root, "attachment.txt");
            await File.WriteAllTextAsync(attachmentPath, "playground attachment content", context.CancellationToken);

            try
            {
                await using var host = context.Host(services => services.AddPinqponqMail(smtp =>
                {
                    smtp.SmtpHost = endpoint.Host!;
                    smtp.SmtpPort = endpoint.Port!.Value;
                    smtp.EnableSsl = false;
                    smtp.FromEmail = "playground@pinqponq.dev";
                    smtp.AttachmentRoot = root;
                }));

                await host.GetRequiredService<IEmailSender>().SendAsync(
                    new EmailMessage
                    {
                        To = "user@pinqponq.dev",
                        Subject = "Mail with attachment",
                        Body = "<p>has an attachment</p>",
                        Attachments = [attachmentPath],
                    },
                    context.CancellationToken);
                context.Step("Attachment under the root sent");

                var mail = await WaitForMailAsync(mailhog, context);
                context.Require("Mail arrived", mail is not null);

                Exception? missing = null;
                try
                {
                    await host.GetRequiredService<IEmailSender>().SendAsync(
                        new EmailMessage
                        {
                            To = "user@pinqponq.dev",
                            Subject = "Missing attachment",
                            Body = "x",
                            Attachments = [Path.Combine(root, "missing.pdf")],
                        },
                        context.CancellationToken);
                }
                catch (ArgumentException exception)
                {
                    missing = exception;
                }

                context.Require("Missing attachment throws ArgumentException", missing is not null);
                context.Artifact("exception", new { type = missing!.GetType().FullName, message = missing.Message });
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                    // temp cleanup best-effort
                }
            }
        });

    private static Scenario MultipleRecipients() => new(
        new ScenarioDescriptor
        {
            Id = "mail.recipients",
            PackageId = Package,
            Title = "Multiple recipients: comma and semicolon",
            Summary = "The To/Cc/Bcc fields accept lists separated by both commas and semicolons. "
                      + "Also verifies that Bcc doesn't appear in the headers.",
            RequiredServices = [DevServiceIds.MailHog],
            Fields =
            [
                new ScenarioField("to", "To", ScenarioFieldKind.Text, "a@pinqponq.dev, b@pinqponq.dev"),
                new ScenarioField("cc", "Cc", ScenarioFieldKind.Text, "c@pinqponq.dev; d@pinqponq.dev"),
                new ScenarioField("bcc", "Bcc", ScenarioFieldKind.Text, "secret@pinqponq.dev"),
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
                    Subject = "Multiple recipients test",
                    Body = "<p>Separator test</p>",
                },
                context.CancellationToken);

            context.Step("Mail sent");

            var messages = await WaitForCountAsync(mailhog, context, expected: 5);
            context.Artifact("delivered copies", messages.Select(message => new
            {
                message.Subject,
                to = message.To,
            }).ToArray(), "table");

            context.Require(
                "Delivered to all five recipients",
                messages.Count == 5,
                $"{messages.Count} copies");

            var headerRecipients = messages
                .SelectMany(message => message.To)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            context.Check(
                "Bcc recipient does not appear in the headers",
                !headerRecipients.Any(recipient =>
                    recipient.Contains("secret@", StringComparison.OrdinalIgnoreCase)),
                string.Join(", ", headerRecipients));
        });

    private static Scenario ConfigurationBinding() => new(
        new ScenarioDescriptor
        {
            Id = "mail.configuration",
            PackageId = Package,
            Title = "Binding from an IConfiguration section",
            Summary = "The AddPinqponqMail(IConfiguration, \"Smtp\") overload binds the section; if "
                      + "the section is missing, it throws a clear InvalidOperationException. No Docker needed.",
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
            context.Require("SmtpHost bound", options.SmtpHost == "smtp.pinqponq.dev", options.SmtpHost);
            context.Require("SmtpPort bound", options.SmtpPort == 587, options.SmtpPort.ToString());
            context.Artifact("bound options", options);

            Exception? thrown = null;
            try
            {
                new ServiceCollection().AddPinqponqMail(configuration, "MissingSection");
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            context.Require("Missing section throws", thrown is not null);
            context.Check(
                "The error names the section",
                thrown!.Message.Contains("MissingSection", StringComparison.Ordinal),
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
