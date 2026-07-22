namespace Pinqponq.Auth.Sso.Abstractions;

/// <summary>
/// Input to an external authentication attempt. Supports both the id-token flow
/// (client already obtained an id_token) and the authorization-code flow
/// (server exchanges a code). Providers use whichever field applies.
/// </summary>
public sealed class ExternalAuthRequest
{
    /// <summary>An OIDC id_token presented by the client, if using the id-token flow.</summary>
    public string? IdToken { get; init; }

    /// <summary>An OAuth2 authorization code, if using the code-exchange flow.</summary>
    public string? AuthorizationCode { get; init; }

    /// <summary>The redirect URI that matches the authorization request (code flow).</summary>
    public string? RedirectUri { get; init; }

    /// <summary>Optional nonce to validate against the token's nonce claim.</summary>
    public string? Nonce { get; init; }

    /// <summary>Creates a request for the id-token flow.</summary>
    public static ExternalAuthRequest FromIdToken(string idToken, string? nonce = null) =>
        new() { IdToken = idToken, Nonce = nonce };

    /// <summary>Creates a request for the authorization-code flow.</summary>
    public static ExternalAuthRequest FromAuthorizationCode(string code, string redirectUri) =>
        new() { AuthorizationCode = code, RedirectUri = redirectUri };
}
