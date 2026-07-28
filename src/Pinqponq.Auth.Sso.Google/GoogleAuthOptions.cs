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

    /// <summary>
    /// Optional Google Workspace hosted domain (<c>hd</c>) that the id_token must match.
    /// When empty, hosted-domain validation is not applied.
    /// </summary>
    public string? HostedDomain { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), authentication fails unless the Google payload
    /// reports <c>email_verified</c>.
    /// </summary>
    public bool RequireEmailVerified { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the request nonce must be present and match the id_token
    /// nonce (replay binding). Defaults to <see langword="false"/> for native/mobile id_token
    /// flows that may not use nonce; enable for browser OIDC.
    /// </summary>
    public bool RequireNonce { get; set; }
}
