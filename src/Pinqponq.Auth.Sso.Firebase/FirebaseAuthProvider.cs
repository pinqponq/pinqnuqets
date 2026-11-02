using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pinqponq.Auth.Sso.Abstractions;

namespace Pinqponq.Auth.Sso.Firebase;

/// <summary>
/// <see cref="IExternalAuthProvider"/> that validates Firebase ID tokens, such as the tokens Firebase
/// returns after Sign in with Apple or after Google sign-in through Firebase.
/// </summary>
public sealed class FirebaseAuthProvider : IExternalAuthProvider
{
    /// <summary>The provider name handled by this implementation.</summary>
    public const string Name = "Firebase";

    /// <summary>The named <see cref="HttpClient"/> that downloads the public keys.</summary>
    public const string HttpClientName = "Pinqponq.Auth.Sso.Firebase";

    private const string IssuerPrefix = "https://securetoken.google.com/";
    private const string InvalidTokenError = "Invalid Firebase ID token.";
    private const string AuthTimeClaim = "auth_time";
    private const string IssuedAtClaim = "iat";
    private const string EmailClaim = "email";
    private const string EmailVerifiedClaim = "email_verified";
    private const string NameClaim = "name";
    private const string PictureClaim = "picture";
    private const string FirebaseClaim = "firebase";
    private const string SignInProviderProperty = "sign_in_provider";

    private readonly FirebaseAuthOptions _firebaseOptions;
    private readonly FirebaseSigningKeyCache _signingKeyCache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FirebaseAuthProvider> _logger;
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private readonly string _expectedIssuer;

    internal FirebaseAuthProvider(
        IOptions<FirebaseAuthOptions> firebaseOptions,
        FirebaseSigningKeyCache signingKeyCache,
        TimeProvider timeProvider,
        ILogger<FirebaseAuthProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(firebaseOptions);
        _firebaseOptions = firebaseOptions.Value;
        _signingKeyCache = signingKeyCache ?? throw new ArgumentNullException(nameof(signingKeyCache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _expectedIssuer = IssuerPrefix + _firebaseOptions.ProjectId;
    }

    /// <inheritdoc />
    public string ProviderName => Name;

    /// <inheritdoc />
    public async Task<ExternalAuthResult> AuthenticateAsync(
        ExternalAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        var firebaseResult = await AuthenticateFirebaseAsync(request, cancellationToken).ConfigureAwait(false);
        return firebaseResult.Result;
    }

    /// <summary>
    /// Authenticates the request like <see cref="AuthenticateAsync"/> and also returns the sign-in method
    /// Firebase reports (<see cref="FirebaseAuthResult.SignInProvider"/>).
    /// </summary>
    /// <exception cref="HttpRequestException">
    /// The public keys could not be downloaded, or the key response is not a JSON Web Key Set.
    /// </exception>
    public async Task<FirebaseAuthResult> AuthenticateFirebaseAsync(
        ExternalAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            _logger.LogDebug("Firebase sign-in rejected: the request has no ID token.");
            return Failure("An ID token is required for Firebase authentication.");
        }

        if (!TryReadToken(request.IdToken, out var unvalidatedToken, out var readException))
        {
            return Rejected("the token could not be read", readException);
        }

        var signingKeys = await _signingKeyCache.GetKeysAsync(unvalidatedToken.Kid, cancellationToken)
            .ConfigureAwait(false);
        var validation = await _tokenHandler
            .ValidateTokenAsync(request.IdToken, CreateValidationParameters(signingKeys))
            .ConfigureAwait(false);
        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken token)
        {
            return Rejected("signature, issuer, audience or lifetime check failed", validation.Exception);
        }

        token.TryGetPayloadValue<bool>(EmailVerifiedClaim, out var isEmailVerified);
        var signInProvider = ReadSignInProvider(token);
        var rejectionReason = FindClaimRejectionReason(token, isEmailVerified, signInProvider);
        if (rejectionReason is not null)
        {
            return Rejected(rejectionReason);
        }

        return CreateResult(token, isEmailVerified, signInProvider);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the token's issuer is this project's Firebase issuer. The token is
    /// not validated, so use this only to choose which provider validates the token, for example between
    /// this provider and a Google provider on one sign-in endpoint.
    /// </summary>
    public bool IsFirebaseIdToken(string idToken)
    {
        if (!TryReadToken(idToken, out var unvalidatedToken, out var readException))
        {
            _logger.LogDebug(readException, "Firebase ID token could not be read to check its issuer.");
            return false;
        }

        return string.Equals(unvalidatedToken.Issuer, _expectedIssuer, StringComparison.Ordinal);
    }

    private bool TryReadToken(
        string idToken,
        [NotNullWhen(true)] out JsonWebToken? token,
        out ArgumentException? readException)
    {
        try
        {
            token = _tokenHandler.ReadJsonWebToken(idToken);
            readException = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            token = null;
            readException = exception;
            return false;
        }
    }

    private TokenValidationParameters CreateValidationParameters(IReadOnlyList<SecurityKey> signingKeys) => new()
    {
        ValidIssuer = _expectedIssuer,
        ValidAudience = _firebaseOptions.ProjectId,
        IssuerSigningKeys = signingKeys,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ClockSkew = _firebaseOptions.ClockSkew,
        LifetimeValidator = IsWithinLifetime,
    };

    private bool IsWithinLifetime(
        DateTime? notBefore,
        DateTime? expires,
        SecurityToken securityToken,
        TokenValidationParameters validationParameters)
    {
        if (expires is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var skew = _firebaseOptions.ClockSkew;
        var hasStarted = notBefore is null || notBefore.Value <= now + skew;
        var hasNotExpired = expires.Value >= now - skew;
        return hasStarted && hasNotExpired;
    }

    private string? FindClaimRejectionReason(JsonWebToken token, bool isEmailVerified, string? signInProvider)
    {
        if (string.IsNullOrWhiteSpace(token.Subject))
        {
            return "the sub claim is missing";
        }

        if (!AreTimeClaimsInThePast(token))
        {
            return "auth_time or iat is missing or in the future";
        }

        if (_firebaseOptions.RequireEmailVerified && !isEmailVerified)
        {
            return "the email is not verified";
        }

        if (!IsSignInProviderAllowed(signInProvider))
        {
            return "the sign-in provider is not allowed";
        }

        return null;
    }

    private static FirebaseAuthResult CreateResult(JsonWebToken token, bool isEmailVerified, string? signInProvider)
    {
        var email = ReadStringClaim(token, EmailClaim);
        var name = ReadStringClaim(token, NameClaim);
        var picture = ReadStringClaim(token, PictureClaim);
        var user = new ExternalUserInfo
        {
            Subject = token.Subject,
            Provider = Name,
            Email = email,
            EmailVerified = isEmailVerified,
            Name = name,
            Picture = picture,
        };
        return new FirebaseAuthResult(ExternalAuthResult.Success(user), signInProvider);
    }

    private bool AreTimeClaimsInThePast(JsonWebToken token)
    {
        if (!token.TryGetPayloadValue<long>(AuthTimeClaim, out var authTimeSeconds)
            || !token.TryGetPayloadValue<long>(IssuedAtClaim, out var issuedAtSeconds))
        {
            return false;
        }

        var latestAcceptedTime = _timeProvider.GetUtcNow() + _firebaseOptions.ClockSkew;
        var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        return authTime <= latestAcceptedTime && issuedAt <= latestAcceptedTime;
    }

    private bool IsSignInProviderAllowed(string? signInProvider) =>
        _firebaseOptions.AllowedSignInProviders.Count == 0
        || (signInProvider is not null
            && _firebaseOptions.AllowedSignInProviders.Contains(signInProvider, StringComparer.Ordinal));

    private static string? ReadSignInProvider(JsonWebToken token)
    {
        if (!token.TryGetPayloadValue<JsonElement>(FirebaseClaim, out var firebaseClaim)
            || firebaseClaim.ValueKind != JsonValueKind.Object
            || !firebaseClaim.TryGetProperty(SignInProviderProperty, out var signInProvider)
            || signInProvider.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return signInProvider.GetString();
    }

    private static string? ReadStringClaim(JsonWebToken token, string claimName) =>
        token.TryGetPayloadValue<string>(claimName, out var value) ? value : null;

    private FirebaseAuthResult Rejected(string reason, Exception? exception = null)
    {
        _logger.LogDebug(exception, "Firebase ID token rejected: {Reason}.", reason);
        return Failure(InvalidTokenError);
    }

    private static FirebaseAuthResult Failure(string error) => new(ExternalAuthResult.Failure(error), null);
}
