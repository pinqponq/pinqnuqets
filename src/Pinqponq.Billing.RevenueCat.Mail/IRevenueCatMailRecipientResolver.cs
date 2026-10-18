namespace Pinqponq.Billing.RevenueCat.Mail;

/// <summary>
/// How the consuming product finds where a subscriber's mail goes. Implemented by the product,
/// usually on top of its user storage.
/// </summary>
public interface IRevenueCatMailRecipientResolver
{
    /// <summary>
    /// Returns the address and language for <paramref name="subscriberId"/>, the id
    /// <see cref="IRevenueCatSubscriptionStore.TryResolveSubscriberId"/> returned. Null when the
    /// subscriber should get no mail (unknown user, no address, opted out); the mail is then
    /// skipped and logged.
    /// </summary>
    Task<RevenueCatMailRecipient?> Resolve(string subscriberId, CancellationToken cancellationToken = default);
}

/// <summary>Where one mail goes.</summary>
/// <param name="Address">The recipient's e-mail address. A value that is not a valid address skips the mail.</param>
/// <param name="Language">
/// The recipient's language, for example <c>tr</c> or <c>pt-BR</c>. Null, or a language without
/// texts, falls back to <see cref="RevenueCatMailOptions.DefaultLanguage"/>.
/// </param>
public sealed record RevenueCatMailRecipient(string Address, string? Language);
