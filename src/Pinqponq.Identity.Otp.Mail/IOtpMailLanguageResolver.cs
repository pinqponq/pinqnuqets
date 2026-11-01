namespace Pinqponq.Identity.Otp.Mail;

/// <summary>
/// Picks the language of the code mail for a recipient, for example from the user's saved
/// preference. The default returns no language, so every mail uses
/// <see cref="OtpMailOptions.DefaultLanguage"/>. A product registers its own implementation to
/// replace it.
/// </summary>
public interface IOtpMailLanguageResolver
{
    /// <summary>
    /// Returns the language key (for example <c>en</c>) for <paramref name="recipient"/>, or
    /// <see langword="null"/> when there is none. A language without texts falls back to
    /// <see cref="OtpMailOptions.DefaultLanguage"/>.
    /// </summary>
    Task<string?> ResolveLanguageAsync(string recipient, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IOtpMailLanguageResolver"/>: never returns a language.</summary>
public sealed class DefaultOtpMailLanguageResolver : IOtpMailLanguageResolver
{
    /// <inheritdoc />
    public Task<string?> ResolveLanguageAsync(string recipient, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
