namespace Pinqponq.Auth.Sso.Firebase;

/// <summary>
/// Configuration for validating Firebase ID tokens.
/// </summary>
public sealed class FirebaseAuthOptions
{
    /// <summary>Default for <see cref="ClockSkew"/>.</summary>
    public static readonly TimeSpan DefaultClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Default for <see cref="KeysCacheDurationWithoutMaxAge"/>.</summary>
    public static readonly TimeSpan DefaultKeysCacheDurationWithoutMaxAge = TimeSpan.FromHours(1);

    /// <summary>Default for <see cref="MinimumUnknownKeyRefreshInterval"/>.</summary>
    public static readonly TimeSpan DefaultMinimumUnknownKeyRefreshInterval = TimeSpan.FromMinutes(1);

    /// <summary>Default for <see cref="PublicKeysUri"/>: the JSON Web Key Set Google publishes for Firebase ID tokens.</summary>
    public static readonly Uri DefaultPublicKeysUri =
        new("https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com");

    /// <summary>
    /// The Firebase project id. The token's issuer must be
    /// <c>https://securetoken.google.com/&lt;ProjectId&gt;</c> and its audience must be the project id.
    /// Required.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// The sign-in methods to accept, compared with the token's <c>firebase.sign_in_provider</c> claim
    /// (for example <c>apple.com</c> or <c>google.com</c>). When empty (default), every sign-in method is accepted.
    /// </summary>
    public IList<string> AllowedSignInProviders { get; } = [];

    /// <summary>
    /// When <see langword="true"/>, authentication fails unless the token's <c>email_verified</c> claim is
    /// <see langword="true"/>. Defaults to <see langword="false"/> because Sign in with Apple can hide the
    /// user's email address.
    /// </summary>
    public bool RequireEmailVerified { get; set; }

    /// <summary>
    /// The time difference allowed between this server's clock and the token's time claims
    /// (<c>exp</c>, <c>nbf</c>, <c>iat</c>, <c>auth_time</c>).
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = DefaultClockSkew;

    /// <summary>
    /// How long the downloaded public keys are kept when the key response sends no
    /// <c>Cache-Control: max-age</c>.
    /// </summary>
    public TimeSpan KeysCacheDurationWithoutMaxAge { get; set; } = DefaultKeysCacheDurationWithoutMaxAge;

    /// <summary>
    /// The shortest time between two key downloads caused by a token whose key id is not in the cache.
    /// Tokens with made-up key ids therefore cannot cause a download per request.
    /// </summary>
    public TimeSpan MinimumUnknownKeyRefreshInterval { get; set; } = DefaultMinimumUnknownKeyRefreshInterval;

    /// <summary>The address of the JSON Web Key Set that signs Firebase ID tokens.</summary>
    public Uri PublicKeysUri { get; set; } = DefaultPublicKeysUri;
}
