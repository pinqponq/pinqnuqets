using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Pinqponq.Auth.Sso.Abstractions;
using Xunit;

namespace Pinqponq.Auth.Sso.Firebase.Tests;

public sealed class FirebaseAuthProviderTests
{
    private const string InvalidTokenError = "Invalid Firebase ID token.";

    [Fact]
    public void ProviderName_is_Firebase()
    {
        using var server = new FirebaseTestServer();

        Assert.Equal("Firebase", server.Provider.ProviderName);
        Assert.Equal("Firebase", FirebaseAuthProvider.Name);
    }

    [Fact]
    public async Task Valid_token_maps_the_user_and_the_sign_in_provider()
    {
        using var server = new FirebaseTestServer();

        var firebaseResult = await server.Provider.AuthenticateFirebaseAsync(
            ExternalAuthRequest.FromIdToken(server.CreateToken()));

        Assert.True(firebaseResult.Result.Succeeded);
        Assert.Null(firebaseResult.Result.Error);
        Assert.Equal("apple.com", firebaseResult.SignInProvider);
        var user = firebaseResult.Result.User!;
        Assert.Equal("firebase-uid-1", user.Subject);
        Assert.Equal("Firebase", user.Provider);
        Assert.Equal("person@example.com", user.Email);
        Assert.True(user.EmailVerified);
        Assert.Equal("Sample Person", user.Name);
        Assert.Equal("https://example.com/picture.png", user.Picture);
        Assert.Null(user.GivenName);
        Assert.Null(user.FamilyName);
    }

    [Fact]
    public async Task AuthenticateAsync_returns_the_shared_result()
    {
        using var server = new FirebaseTestServer();

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));

        Assert.True(result.Succeeded);
        Assert.Equal("firebase-uid-1", result.User!.Subject);
    }

    [Fact]
    public async Task Token_without_optional_claims_succeeds_with_empty_profile_fields()
    {
        using var server = new FirebaseTestServer();
        var token = server.CreateToken(claims =>
        {
            claims.Remove("email");
            claims.Remove("email_verified");
            claims.Remove("name");
            claims.Remove("picture");
            claims.Remove("firebase");
        });

        var firebaseResult = await server.Provider.AuthenticateFirebaseAsync(ExternalAuthRequest.FromIdToken(token));

        Assert.True(firebaseResult.Result.Succeeded);
        var user = firebaseResult.Result.User!;
        Assert.Null(user.Email);
        Assert.False(user.EmailVerified);
        Assert.Null(user.Name);
        Assert.Null(user.Picture);
        Assert.Null(firebaseResult.SignInProvider);
    }

    [Theory]
    [InlineData("iss", "https://securetoken.google.com/other-project")]
    [InlineData("iss", "https://accounts.google.com")]
    [InlineData("aud", "other-project")]
    [InlineData("sub", "")]
    public async Task Token_with_wrong_claim_fails(string claimName, string claimValue)
    {
        using var server = new FirebaseTestServer();
        var token = server.CreateToken(claims => claims[claimName] = claimValue);

        await AssertInvalidAsync(server, token);
    }

    [Fact]
    public async Task Token_without_subject_fails()
    {
        using var server = new FirebaseTestServer();

        await AssertInvalidAsync(server, server.CreateToken(claims => claims.Remove("sub")));

        AssertRejectionLogged(server, "the sub claim is missing");
    }

    [Fact]
    public async Task Missing_issued_at_fails()
    {
        using var server = new FirebaseTestServer();

        await AssertInvalidAsync(server, server.CreateToken(claims => claims.Remove("iat")));

        AssertRejectionLogged(server, "auth_time or iat is missing or in the future");
    }

    [Fact]
    public async Task Expired_token_fails()
    {
        using var server = new FirebaseTestServer();
        var token = server.CreateToken();
        server.Time.Advance(TimeSpan.FromHours(1) + FirebaseAuthOptions.DefaultClockSkew + TimeSpan.FromSeconds(1));

        await AssertInvalidAsync(server, token);
    }

    [Fact]
    public async Task Token_expired_exactly_by_the_clock_skew_succeeds()
    {
        using var server = new FirebaseTestServer();
        var token = server.CreateToken();
        server.Time.Advance(TimeSpan.FromHours(1) + FirebaseAuthOptions.DefaultClockSkew);

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(token));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Not_before_later_than_the_clock_skew_fails()
    {
        using var server = new FirebaseTestServer();
        var notBefore = server.Time.GetUtcNow() + FirebaseAuthOptions.DefaultClockSkew + TimeSpan.FromSeconds(1);
        var token = server.CreateToken(claims => claims["nbf"] = notBefore.ToUnixTimeSeconds());

        await AssertInvalidAsync(server, token);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    [InlineData(300)]
    public async Task Not_before_up_to_the_clock_skew_succeeds(int secondsFromNow)
    {
        using var server = new FirebaseTestServer();
        var notBefore = server.Time.GetUtcNow().AddSeconds(secondsFromNow);
        var token = server.CreateToken(claims => claims["nbf"] = notBefore.ToUnixTimeSeconds());

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(token));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Issued_at_exactly_at_the_clock_skew_succeeds()
    {
        using var server = new FirebaseTestServer();
        var issuedAt = server.Time.GetUtcNow() + FirebaseAuthOptions.DefaultClockSkew;
        var token = server.CreateToken(claims => claims["iat"] = issuedAt.ToUnixTimeSeconds());

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(token));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Token_without_expiry_fails()
    {
        using var server = new FirebaseTestServer();

        await AssertInvalidAsync(server, server.CreateToken(claims => claims.Remove("exp")));
    }

    [Fact]
    public async Task Auth_time_in_the_future_fails()
    {
        using var server = new FirebaseTestServer();
        var authTime = server.Time.GetUtcNow() + FirebaseAuthOptions.DefaultClockSkew + TimeSpan.FromSeconds(1);
        var token = server.CreateToken(claims => claims["auth_time"] = authTime.ToUnixTimeSeconds());

        await AssertInvalidAsync(server, token);
    }

    [Fact]
    public async Task Auth_time_within_the_clock_skew_succeeds()
    {
        using var server = new FirebaseTestServer();
        var authTime = server.Time.GetUtcNow() + FirebaseAuthOptions.DefaultClockSkew;
        var token = server.CreateToken(claims => claims["auth_time"] = authTime.ToUnixTimeSeconds());

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(token));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Missing_auth_time_fails()
    {
        using var server = new FirebaseTestServer();

        await AssertInvalidAsync(server, server.CreateToken(claims => claims.Remove("auth_time")));

        AssertRejectionLogged(server, "auth_time or iat is missing or in the future");
    }

    [Fact]
    public async Task Issued_at_in_the_future_fails()
    {
        using var server = new FirebaseTestServer();
        var issuedAt = server.Time.GetUtcNow() + FirebaseAuthOptions.DefaultClockSkew + TimeSpan.FromSeconds(1);
        var token = server.CreateToken(claims => claims["iat"] = issuedAt.ToUnixTimeSeconds());

        await AssertInvalidAsync(server, token);
    }

    [Fact]
    public async Task Configured_clock_skew_is_used()
    {
        using var server = new FirebaseTestServer(options => options.ClockSkew = TimeSpan.Zero);
        var token = server.CreateToken();
        server.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        await AssertInvalidAsync(server, token);
    }

    [Fact]
    public async Task Token_signed_by_a_key_that_is_not_published_fails()
    {
        using var server = new FirebaseTestServer();
        server.AddSigningKey("key-2");

        await AssertInvalidAsync(server, server.CreateToken(keyId: "key-2"));
    }

    [Fact]
    public async Task Token_with_a_tampered_payload_fails()
    {
        using var server = new FirebaseTestServer();
        var parts = server.CreateToken().Split('.');
        var otherPayload = server.CreateToken(claims => claims["sub"] = "someone-else").Split('.')[1];
        var tamperedToken = string.Join('.', parts[0], otherPayload, parts[2]);

        await AssertInvalidAsync(server, tamperedToken);
    }

    [Fact]
    public async Task Unknown_key_id_downloads_the_keys_once_more()
    {
        using var server = new FirebaseTestServer();
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        server.AddSigningKey("key-2");
        server.PublishedKeyIds = ["key-1", "key-2"];
        server.Time.Advance(FirebaseAuthOptions.DefaultMinimumUnknownKeyRefreshInterval);

        var result = await server.Provider.AuthenticateAsync(
            ExternalAuthRequest.FromIdToken(server.CreateToken(keyId: "key-2")));

        Assert.True(result.Succeeded);
        Assert.Equal(2, server.KeyEndpoint.RequestedUris.Count);
    }

    [Fact]
    public async Task Unknown_key_id_does_not_download_again_within_the_minimum_interval()
    {
        using var server = new FirebaseTestServer();
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        server.AddSigningKey("key-2");
        server.Time.Advance(FirebaseAuthOptions.DefaultMinimumUnknownKeyRefreshInterval - TimeSpan.FromSeconds(1));

        await AssertInvalidAsync(server, server.CreateToken(keyId: "key-2"));
        await AssertInvalidAsync(server, server.CreateToken(keyId: "key-2"));

        Assert.Single(server.KeyEndpoint.RequestedUris);
    }

    [Fact]
    public async Task Unknown_key_id_downloads_only_once_when_the_key_stays_unknown()
    {
        using var server = new FirebaseTestServer();
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        server.AddSigningKey("key-2");
        server.Time.Advance(FirebaseAuthOptions.DefaultMinimumUnknownKeyRefreshInterval);

        await AssertInvalidAsync(server, server.CreateToken(keyId: "key-2"));
        await AssertInvalidAsync(server, server.CreateToken(keyId: "key-2"));

        Assert.Equal(2, server.KeyEndpoint.RequestedUris.Count);
    }

    [Fact]
    public async Task Every_published_key_is_found_in_the_cache()
    {
        using var server = new FirebaseTestServer();
        server.AddSigningKey("key-2");
        server.PublishedKeyIds = ["key-1", "key-2"];
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        server.Time.Advance(FirebaseAuthOptions.DefaultMinimumUnknownKeyRefreshInterval);

        var result = await server.Provider.AuthenticateAsync(
            ExternalAuthRequest.FromIdToken(server.CreateToken(keyId: "key-2")));

        Assert.True(result.Succeeded);
        Assert.Single(server.KeyEndpoint.RequestedUris);
    }

    [Fact]
    public async Task Keys_are_cached_between_calls()
    {
        using var server = new FirebaseTestServer();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
            Assert.True(result.Succeeded);
        }

        var requestedUri = Assert.Single(server.KeyEndpoint.RequestedUris);
        Assert.Equal(FirebaseAuthOptions.DefaultPublicKeysUri, requestedUri);
    }

    [Fact]
    public async Task Keys_are_downloaded_again_when_max_age_has_passed()
    {
        using var server = new FirebaseTestServer();
        server.KeyEndpoint.MaxAge = TimeSpan.FromMinutes(10);
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));

        server.Time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        Assert.Single(server.KeyEndpoint.RequestedUris);

        server.Time.Advance(TimeSpan.FromSeconds(1));
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        Assert.Equal(2, server.KeyEndpoint.RequestedUris.Count);
    }

    [Fact]
    public async Task Keys_without_max_age_are_kept_for_the_configured_duration()
    {
        using var server = new FirebaseTestServer(options =>
            options.KeysCacheDurationWithoutMaxAge = TimeSpan.FromMinutes(30));
        server.KeyEndpoint.MaxAge = null;
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));

        server.Time.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        Assert.Single(server.KeyEndpoint.RequestedUris);

        server.Time.Advance(TimeSpan.FromSeconds(1));
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        Assert.Equal(2, server.KeyEndpoint.RequestedUris.Count);
    }

    [Fact]
    public async Task Configured_public_keys_uri_is_requested()
    {
        var keysUri = new Uri("https://keys.example.com/jwks");
        using var server = new FirebaseTestServer(options => options.PublicKeysUri = keysUri);

        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));

        Assert.Equal(keysUri, Assert.Single(server.KeyEndpoint.RequestedUris));
    }

    [Fact]
    public async Task Failed_key_download_throws()
    {
        using var server = new FirebaseTestServer();
        server.KeyEndpoint.StatusCode = HttpStatusCode.ServiceUnavailable;

        var act = () => server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));

        await Assert.ThrowsAsync<HttpRequestException>(act);
    }

    [Fact]
    public async Task Key_response_that_is_not_a_key_set_throws()
    {
        using var server = new FirebaseTestServer();
        server.KeyEndpoint.KeySetJson = () => "not json";

        var act = () => server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Equal("The public key response is not a JSON Web Key Set.", exception.Message);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task RequireEmailVerified_rejects_an_unverified_email()
    {
        using var server = new FirebaseTestServer(options => options.RequireEmailVerified = true);

        await AssertInvalidAsync(server, server.CreateToken(claims => claims["email_verified"] = false));
        await AssertInvalidAsync(server, server.CreateToken(claims => claims.Remove("email_verified")));

        AssertRejectionLogged(server, "the email is not verified");
    }

    [Fact]
    public async Task RequireEmailVerified_accepts_a_verified_email()
    {
        using var server = new FirebaseTestServer(options => options.RequireEmailVerified = true);

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Unverified_email_is_accepted_by_default()
    {
        using var server = new FirebaseTestServer();
        var token = server.CreateToken(claims => claims["email_verified"] = false);

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(token));

        Assert.True(result.Succeeded);
        Assert.False(result.User!.EmailVerified);
    }

    [Fact]
    public async Task AllowedSignInProviders_accepts_a_listed_provider()
    {
        using var server = new FirebaseTestServer(options =>
        {
            options.AllowedSignInProviders.Add("google.com");
            options.AllowedSignInProviders.Add("apple.com");
        });

        var firebaseResult = await server.Provider.AuthenticateFirebaseAsync(
            ExternalAuthRequest.FromIdToken(server.CreateToken()));

        Assert.True(firebaseResult.Result.Succeeded);
        Assert.Equal("apple.com", firebaseResult.SignInProvider);
    }

    [Fact]
    public async Task AllowedSignInProviders_rejects_other_or_missing_providers()
    {
        using var server = new FirebaseTestServer(options => options.AllowedSignInProviders.Add("google.com"));

        await AssertInvalidAsync(server, server.CreateToken());
        await AssertInvalidAsync(server, server.CreateToken(claims => claims.Remove("firebase")));
        await AssertInvalidAsync(server, server.CreateToken(claims => claims["firebase"] = "google.com"));
        await AssertInvalidAsync(server, server.CreateToken(claims =>
            claims["firebase"] = new Dictionary<string, object> { ["sign_in_provider"] = 5 }));

        AssertRejectionLogged(server, "the sign-in provider is not allowed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Empty_id_token_fails(string idToken)
    {
        using var server = new FirebaseTestServer();

        var result = await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(idToken));

        Assert.False(result.Succeeded);
        Assert.Equal("An ID token is required for Firebase authentication.", result.Error);
        Assert.Empty(server.KeyEndpoint.RequestedUris);
        Assert.Contains(
            server.Logs.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message == "Firebase ID token rejected: the request has no ID token.");
    }

    [Fact]
    public async Task Authorization_code_without_id_token_fails()
    {
        using var server = new FirebaseTestServer();

        var result = await server.Provider.AuthenticateAsync(
            ExternalAuthRequest.FromAuthorizationCode("code", "https://app.example.com/callback"));

        Assert.False(result.Succeeded);
        Assert.Equal("An ID token is required for Firebase authentication.", result.Error);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("not.a.valid.jwt")]
    [InlineData("abc.def.ghi")]
    public async Task Malformed_token_fails_without_downloading_keys(string idToken)
    {
        using var server = new FirebaseTestServer();

        await AssertInvalidAsync(server, idToken);

        Assert.Empty(server.KeyEndpoint.RequestedUris);
        AssertRejectionLogged(server, "the token could not be read");
    }

    [Fact]
    public async Task Null_request_throws()
    {
        using var server = new FirebaseTestServer();

        await Assert.ThrowsAsync<ArgumentNullException>(() => server.Provider.AuthenticateAsync(null!));
    }

    [Fact]
    public async Task Cancelled_token_throws()
    {
        using var server = new FirebaseTestServer();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => server.Provider.AuthenticateAsync(
            ExternalAuthRequest.FromIdToken(server.CreateToken()),
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task Cancelled_token_throws_when_the_keys_are_cached()
    {
        using var server = new FirebaseTestServer();
        await server.Provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(server.CreateToken()));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => server.Provider.AuthenticateAsync(
            ExternalAuthRequest.FromIdToken(server.CreateToken()),
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task Rejection_reason_and_validation_exception_are_logged()
    {
        using var server = new FirebaseTestServer();

        await AssertInvalidAsync(server, server.CreateToken(claims => claims["aud"] = "other-project"));

        var entry = Assert.Single(server.Logs.Entries, logEntry => logEntry.Level == LogLevel.Debug);
        Assert.Equal(
            "Firebase ID token rejected: signature, issuer, audience or lifetime check failed.",
            entry.Message);
        Assert.NotNull(entry.Exception);
    }

    [Fact]
    public void Constructors_reject_null_arguments()
    {
        var firebaseOptions = Options.Create(new FirebaseAuthOptions { ProjectId = FirebaseTestServer.ProjectId });
        var timeProvider = new FakeTimeProvider();
        using var httpClientFactory = new StubHttpClientFactory();
        using var signingKeyCache = new FirebaseSigningKeyCache(httpClientFactory, firebaseOptions, timeProvider);
        var logger = NullLogger<FirebaseAuthProvider>.Instance;

        Assert.Throws<ArgumentNullException>(() => new FirebaseAuthProvider(null!, signingKeyCache, timeProvider, logger));
        Assert.Throws<ArgumentNullException>(() => new FirebaseAuthProvider(firebaseOptions, null!, timeProvider, logger));
        Assert.Throws<ArgumentNullException>(() => new FirebaseAuthProvider(firebaseOptions, signingKeyCache, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new FirebaseAuthProvider(firebaseOptions, signingKeyCache, timeProvider, null!));
        Assert.Throws<ArgumentNullException>(() => new FirebaseSigningKeyCache(null!, firebaseOptions, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new FirebaseSigningKeyCache(httpClientFactory, null!, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new FirebaseSigningKeyCache(httpClientFactory, firebaseOptions, null!));
    }

    [Fact]
    public void IsFirebaseIdToken_is_true_for_this_projects_issuer()
    {
        using var server = new FirebaseTestServer();

        Assert.True(server.Provider.IsFirebaseIdToken(server.CreateToken()));
        Assert.Empty(server.KeyEndpoint.RequestedUris);
    }

    [Theory]
    [InlineData("https://accounts.google.com")]
    [InlineData("https://securetoken.google.com/other-project")]
    [InlineData("https://securetoken.google.com/sample-project/extra")]
    public void IsFirebaseIdToken_is_false_for_other_issuers(string issuer)
    {
        using var server = new FirebaseTestServer();

        Assert.False(server.Provider.IsFirebaseIdToken(server.CreateToken(claims => claims["iss"] = issuer)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("abc.def.ghi")]
    public void IsFirebaseIdToken_is_false_for_malformed_tokens(string idToken)
    {
        using var server = new FirebaseTestServer();

        Assert.False(server.Provider.IsFirebaseIdToken(idToken));

        Assert.Contains(
            server.Logs.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message == "Firebase ID token could not be read to check its issuer.");
    }

    private static async Task AssertInvalidAsync(FirebaseTestServer server, string idToken)
    {
        var firebaseResult = await server.Provider.AuthenticateFirebaseAsync(ExternalAuthRequest.FromIdToken(idToken));

        Assert.False(firebaseResult.Result.Succeeded);
        Assert.Null(firebaseResult.Result.User);
        Assert.Equal(InvalidTokenError, firebaseResult.Result.Error);
        Assert.Null(firebaseResult.SignInProvider);
        Assert.Contains(
            server.Logs.Entries,
            entry => entry.Level == LogLevel.Debug && entry.Message.StartsWith("Firebase ID token rejected: "));
    }

    private static void AssertRejectionLogged(FirebaseTestServer server, string reason) =>
        Assert.Contains(
            server.Logs.Entries,
            entry => entry.Level == LogLevel.Debug && entry.Message == $"Firebase ID token rejected: {reason}.");

    private sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _httpClient = new();

        public HttpClient CreateClient(string name) => _httpClient;

        public void Dispose() => _httpClient.Dispose();
    }
}
