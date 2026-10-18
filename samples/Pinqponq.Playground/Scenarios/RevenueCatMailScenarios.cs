using Pinqponq.Billing.RevenueCat;
using Pinqponq.Billing.RevenueCat.DependencyInjection;
using Pinqponq.Billing.RevenueCat.Mail;
using Pinqponq.Billing.RevenueCat.Mail.DependencyInjection;
using Pinqponq.Mail.DependencyInjection;
using Pinqponq.Playground.Infrastructure;
using Pinqponq.Playground.Scenarios.Support;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for <c>Pinqponq.Billing.RevenueCat.Mail</c>: webhook events go through the real
/// handler and the mail lands in the MailHog container.
/// </summary>
public static class RevenueCatMailScenarios
{
    private const string Package = "Pinqponq.Billing.RevenueCat.Mail";
    private const string Entitlement = "premium";

    public static IEnumerable<Scenario> Create()
    {
        yield return PurchaseAndBillingIssueMails();
    }

    private static Scenario PurchaseAndBillingIssueMails() => new(
        new ScenarioDescriptor
        {
            Id = "revenuecat.mail.send",
            PackageId = Package,
            Title = "Subscription mails to MailHog",
            Summary = "INITIAL_PURCHASE and BILLING_ISSUE webhooks send branded mails (HTML plus plain text) "
                      + "to the resolved recipient; a retried delivery sends nothing and RENEWAL stays off "
                      + "because it is not enabled.",
            RequiredServices = [DevServiceIds.MailHog],
            Fields =
            [
                new ScenarioField("to", "Recipient", ScenarioFieldKind.Text, "subscriber@pinqponq.dev"),
                new ScenarioField("language", "Recipient language", ScenarioFieldKind.Text, "tr-TR"),
                new ScenarioField("brand", "Brand name", ScenarioFieldKind.Text, "Playground"),
            ],
            TimeoutSeconds = 45,
        },
        async context =>
        {
            var mailhog = context.AppServices.GetRequiredService<MailHogClient>();
            await mailhog.ClearAsync(context.CancellationToken);
            var endpoint = context.Stack.Require(DevServiceIds.MailHog);
            var resolver = new FixedRecipientResolver(
                new RevenueCatMailRecipient(context.Input.Text("to"), context.Input.Text("language")));

            await using var host = context.Host(services => services
                .AddPinqponqMail(smtp =>
                {
                    smtp.SmtpHost = endpoint.Host!;
                    smtp.SmtpPort = endpoint.Port!.Value;
                    smtp.EnableSsl = false;
                    smtp.FromEmail = "playground@pinqponq.dev";
                })
                .AddSingleton<IRevenueCatSubscriptionStore>(new InMemoryRevenueCatStore())
                .AddPinqponqRevenueCat<InMemoryRevenueCatStore>(options =>
                {
                    options.WebhookAuthHeader = "Bearer playground";
                    options.PremiumEntitlementId = Entitlement;
                })
                .AddSingleton<IRevenueCatMailRecipientResolver>(resolver)
                .AddPinqponqRevenueCatMail<FixedRecipientResolver>(options =>
                {
                    options.Brand.Name = context.Input.Text("brand");
                    options.Brand.FooterText = "© {BrandName}";
                    options.Events[RevenueCatMailEvent.BillingIssue].Enabled = true;
                    options.Events[RevenueCatMailEvent.BillingIssue].CallToActionUrl = "https://example.com/account";
                    options.Texts["en"].Events[RevenueCatMailEvent.BillingIssue].CallToActionLabel = "Update payment";
                    options.Texts["tr"] = new RevenueCatMailTexts
                    {
                        Events =
                        {
                            [RevenueCatMailEvent.InitialPurchase] = new RevenueCatMailText
                            {
                                Subject = "{BrandName} aboneliğin aktif",
                                Preheader = "{BrandName} aboneliğin için teşekkürler.",
                                Title = "Abone olduğun için teşekkürler",
                                Paragraphs = { "{BrandName} aboneliğin artık aktif." },
                            },
                        },
                    };
                }));

            var handler = host.GetRequiredService<IRevenueCatEventHandler>();
            await handler.Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase), context.CancellationToken);
            await handler.Handle(Payload("evt-1", RevenueCatEventType.InitialPurchase), context.CancellationToken);
            context.Step("INITIAL_PURCHASE handled twice (second is a retried delivery)");
            await handler.Handle(Payload("evt-2", RevenueCatEventType.Renewal), context.CancellationToken);
            context.Step("RENEWAL handled (not enabled)");
            await handler.Handle(Payload("evt-3", RevenueCatEventType.BillingIssue), context.CancellationToken);
            context.Step("BILLING_ISSUE handled");

            var messages = await MailHogInbox.WaitForCount(mailhog, expected: 2, context.CancellationToken);
            context.Artifact("delivered mails", messages.Select(message => new { message.Subject, message.To }).ToArray(), "table");
            context.Require("Exactly two mails arrived", messages.Count == 2, $"{messages.Count} mails");

            var brand = context.Input.Text("brand");
            context.Check(
                "Purchase mail uses the recipient's base language",
                messages.Any(message => message.Subject == $"{brand} aboneliğin aktif"),
                string.Join(" | ", messages.Select(message => message.Subject)));
            context.Check(
                "Billing issue mail falls back to English",
                messages.Any(message => message.Subject == $"Payment problem with your {brand} subscription"),
                string.Join(" | ", messages.Select(message => message.Subject)));
            context.Check(
                "Mails carry an HTML and a plain-text part",
                messages.All(message => message.RawBody.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                                        && message.RawBody.Contains("text/plain", StringComparison.OrdinalIgnoreCase)));
            context.Artifact("billing issue mail", messages.FirstOrDefault(message => message.Subject.StartsWith("Payment", StringComparison.Ordinal))?.RawBody);
        });

    private static RevenueCatWebhookPayload Payload(string eventId, string type) => new()
    {
        ApiVersion = "1.0",
        Event = new RevenueCatEvent
        {
            Id = eventId,
            Type = type,
            EventTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AppUserId = "21",
            EntitlementIds = [Entitlement],
            ProductId = "pro_monthly",
            Store = "PLAY_STORE",
            OriginalTransactionId = "transaction-1",
            ExpirationAtMs = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds(),
        },
    };

    /// <summary>The recipient lookup the package leaves to the product; here one fixed recipient.</summary>
    private sealed class FixedRecipientResolver(RevenueCatMailRecipient recipient) : IRevenueCatMailRecipientResolver
    {
        public Task<RevenueCatMailRecipient?> Resolve(string subscriberId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RevenueCatMailRecipient?>(recipient);
    }
}
