using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pinqponq.Auth.Sso.Firebase.DependencyInjection;

namespace Pinqponq.Auth.Sso.Firebase.Tests;

/// <summary>
/// Signs Firebase-shaped ID tokens with local RSA keys and serves those keys from a fake key endpoint.
/// </summary>
internal sealed class FirebaseTestServer : IDisposable
{
    public const string ProjectId = "sample-project";
    public const string Issuer = "https://securetoken.google.com/" + ProjectId;

    private readonly Dictionary<string, RsaSecurityKey> _signingKeys = [];
    private readonly ServiceProvider _serviceProvider;

    public FirebaseTestServer(Action<FirebaseAuthOptions>? configure = null)
    {
        AddSigningKey("key-1");
        PublishedKeyIds = ["key-1"];
        KeyEndpoint.KeySetJson = CreateKeySetJson;

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddPinqponqFirebaseSso(options =>
        {
            options.ProjectId = ProjectId;
            configure?.Invoke(options);
        });
        services.AddHttpClient(FirebaseAuthProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => KeyEndpoint);
        _serviceProvider = services.BuildServiceProvider();
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    public KeyEndpointHandler KeyEndpoint { get; } = new();

    /// <summary>The key ids the fake endpoint returns.</summary>
    public IList<string> PublishedKeyIds { get; set; }

    public FirebaseAuthProvider Provider => _serviceProvider.GetRequiredService<FirebaseAuthProvider>();

    public void AddSigningKey(string keyId)
    {
        var rsa = RSA.Create(2048);
        _signingKeys[keyId] = new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    public string CreateToken(Action<Dictionary<string, object>>? changeClaims = null, string keyId = "key-1")
    {
        var now = Time.GetUtcNow();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = ProjectId,
            ["sub"] = "firebase-uid-1",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["auth_time"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["email"] = "person@example.com",
            ["email_verified"] = true,
            ["name"] = "Sample Person",
            ["picture"] = "https://example.com/picture.png",
            ["firebase"] = new Dictionary<string, object> { ["sign_in_provider"] = "apple.com" },
        };
        changeClaims?.Invoke(claims);

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = new SigningCredentials(_signingKeys[keyId], SecurityAlgorithms.RsaSha256),
        };
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateToken(descriptor);
    }

    public string CreateKeySetJson()
    {
        var keys = PublishedKeyIds
            .Select(keyId => _signingKeys[keyId])
            .Select(signingKey =>
            {
                var parameters = signingKey.Rsa.ExportParameters(includePrivateParameters: false);
                return new Dictionary<string, string>
                {
                    ["kty"] = "RSA",
                    ["alg"] = "RS256",
                    ["use"] = "sig",
                    ["kid"] = signingKey.KeyId,
                    ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                    ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
                };
            });
        return JsonSerializer.Serialize(new { keys });
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        foreach (var signingKey in _signingKeys.Values)
        {
            signingKey.Rsa.Dispose();
        }
    }

    internal sealed class KeyEndpointHandler : HttpMessageHandler
    {
        public Func<string>? KeySetJson { get; set; }

        public TimeSpan? MaxAge { get; set; } = TimeSpan.FromHours(6);

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public List<Uri?> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri);
            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(KeySetJson?.Invoke() ?? "{}"),
            };
            if (MaxAge is { } maxAge)
            {
                response.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = maxAge };
            }

            return Task.FromResult(response);
        }
    }
}
