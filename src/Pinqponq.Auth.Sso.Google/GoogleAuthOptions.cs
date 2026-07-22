namespace Pinqponq.Auth.Sso.Google;

/// <summary>
/// Configuration for validating Google id_tokens.
/// </summary>
public sealed class GoogleAuthOptions
{
    /// <summary>
    /// Accepted audiences — the Google OAuth client id(s) the id_token must be issued for.
    /// At least one is required.
    /// </summary>
    public IList<string> ClientIds { get; } = [];
}
