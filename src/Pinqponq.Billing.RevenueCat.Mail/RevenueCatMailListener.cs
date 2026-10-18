using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Mail;

namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>
/// Sends the mail of an enabled <see cref="RevenueCatMailEvent"/> once the event is committed.
/// A failure to resolve, render or send is logged and never thrown, so a mail problem does not
/// fail the webhook; only a cancellation of the webhook request propagates, after it is logged.
/// </summary>
public sealed class RevenueCatMailListener(
    IRevenueCatMailRecipientResolver recipientResolver,
    IRevenueCatMailRenderer renderer,
    IEmailSender emailSender,
    IOptions<RevenueCatMailOptions> options,
    IRevenueCatWebhookObserver observer,
    ILogger<RevenueCatMailListener> logger) : IRevenueCatEventListener
{
    private readonly RevenueCatMailOptions _options = options.Value;

    public async Task OnEventProcessed(RevenueCatEventContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!RevenueCatMailEventMapping.TryMap(context.Event.Type, out var mailEvent)
            || !_options.Events.TryGetValue(mailEvent, out var eventOptions)
            || !eventOptions.Enabled)
        {
            return;
        }

        if (_options.RequirePremiumEntitlement && !context.IsPremiumEntitlement)
        {
            LogSkipped(context, mailEvent, RevenueCatMailSkipReasons.EntitlementNotPremium);
            return;
        }

        if (context.SubscriberId is null)
        {
            LogSkipped(context, mailEvent, RevenueCatMailSkipReasons.SubscriberUnresolved);
            return;
        }

        try
        {
            await Send(context, context.SubscriberId, mailEvent, eventOptions, cancellationToken);
        }
        catch (OperationCanceledException exception) when (IsRequestCancellation(exception, cancellationToken))
        {
            LogFailed(context, mailEvent, exception);
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(context, mailEvent, exception);
        }
    }

    private async Task Send(
        RevenueCatEventContext context,
        string subscriberId,
        RevenueCatMailEvent mailEvent,
        RevenueCatMailEventOptions eventOptions,
        CancellationToken cancellationToken)
    {
        var recipient = await recipientResolver.Resolve(subscriberId, cancellationToken);
        if (recipient is null)
        {
            LogSkipped(context, mailEvent, RevenueCatMailSkipReasons.RecipientUnresolved);
            return;
        }

        if (!IsValidAddress(recipient.Address))
        {
            LogSkipped(context, mailEvent, RevenueCatMailSkipReasons.RecipientAddressInvalid);
            return;
        }

        var (language, text) = ResolveText(recipient.Language, mailEvent);
        var request = new RevenueCatMailRenderRequest(mailEvent, language, text, _options.Brand, eventOptions, context);
        var renderedMail = renderer.Render(request);
        var message = new EmailMessage
        {
            To = recipient.Address,
            Subject = renderedMail.Subject,
            Body = renderedMail.HtmlBody,
            IsBodyHtml = true,
            PlainTextBody = renderedMail.PlainTextBody,
        };

        await emailSender.SendAsync(message, cancellationToken);
        LogSent(context, mailEvent, language);
    }

    private (string Language, RevenueCatMailText Text) ResolveText(string? recipientLanguage, RevenueCatMailEvent mailEvent)
    {
        foreach (var language in CandidateLanguages(recipientLanguage))
        {
            if (_options.Texts.TryGetValue(language, out var texts) && texts.Events.TryGetValue(mailEvent, out var text))
            {
                return (language, text);
            }
        }

        throw new InvalidOperationException(
            $"No text for {mailEvent} in the default language '{_options.DefaultLanguage}'.");
    }

    private IEnumerable<string> CandidateLanguages(string? recipientLanguage)
    {
        if (!string.IsNullOrWhiteSpace(recipientLanguage))
        {
            yield return recipientLanguage;

            var separatorIndex = recipientLanguage.IndexOf('-', StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                yield return recipientLanguage[..separatorIndex];
            }
        }

        yield return _options.DefaultLanguage;
    }

    private static bool IsValidAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) && MailAddress.TryCreate(address, out _);

    private static bool IsRequestCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private void LogSent(RevenueCatEventContext context, RevenueCatMailEvent mailEvent, string language)
    {
        logger.LogInformation("RevenueCat {MailEvent} mail sent for subscriber {SubscriberId} (event {EventId}, language {Language}).",
            mailEvent, context.SubscriberId, context.Event.Id, language);
        Observe(RevenueCatWebhookLogLevel.Info, RevenueCatMailLogEvents.Sent, "RevenueCat subscription mail sent.",
            context, mailEvent,
            (RevenueCatMailLogKeys.Language, language));
    }

    private void LogSkipped(RevenueCatEventContext context, RevenueCatMailEvent mailEvent, string reason)
    {
        logger.LogInformation("RevenueCat {MailEvent} mail skipped for subscriber {SubscriberId} (event {EventId}): {Reason}.",
            mailEvent, context.SubscriberId, context.Event.Id, reason);
        Observe(RevenueCatWebhookLogLevel.Info, RevenueCatMailLogEvents.Skipped, "RevenueCat subscription mail skipped.",
            context, mailEvent,
            (RevenueCatMailLogKeys.Reason, reason));
    }

    private void LogFailed(RevenueCatEventContext context, RevenueCatMailEvent mailEvent, Exception exception)
    {
        logger.LogError(exception, "RevenueCat {MailEvent} mail failed for subscriber {SubscriberId} (event {EventId}).",
            mailEvent, context.SubscriberId, context.Event.Id);
        Observe(RevenueCatWebhookLogLevel.Warning, RevenueCatMailLogEvents.Failed, "RevenueCat subscription mail failed.",
            context, mailEvent,
            (RevenueCatMailLogKeys.ExceptionType, exception.GetType().FullName));
    }

    private void Observe(
        RevenueCatWebhookLogLevel level,
        string eventName,
        string message,
        RevenueCatEventContext context,
        RevenueCatMailEvent mailEvent,
        (string Key, object? Value) detail)
    {
        var data = new Dictionary<string, object?>
        {
            [RevenueCatMailLogKeys.EventId] = context.Event.Id,
            [RevenueCatMailLogKeys.EventType] = context.Event.Type,
            [RevenueCatMailLogKeys.SubscriberId] = context.SubscriberId,
            [RevenueCatMailLogKeys.MailEvent] = mailEvent.ToString(),
            [detail.Key] = detail.Value,
        };
        observer.Observe(new RevenueCatWebhookLog(level, eventName, message, data));
    }
}
