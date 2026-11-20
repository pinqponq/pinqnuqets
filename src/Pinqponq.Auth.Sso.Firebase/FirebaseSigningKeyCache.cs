using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Pinqponq.Auth.Sso.Firebase;

/// <summary>
/// Downloads the public keys that sign Firebase ID tokens and keeps them for the
/// <c>max-age</c> the key response sends.
/// </summary>
internal sealed class FirebaseSigningKeyCache : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FirebaseAuthOptions _firebaseOptions;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private CachedKeys? _cachedKeys;

    public FirebaseSigningKeyCache(
        IHttpClientFactory httpClientFactory,
        IOptions<FirebaseAuthOptions> firebaseOptions,
        TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        ArgumentNullException.ThrowIfNull(firebaseOptions);
        _firebaseOptions = firebaseOptions.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Returns the cached keys. Downloads them when the cache is empty or expired, and once more when
    /// <paramref name="keyId"/> is not among them, at most once per
    /// <see cref="FirebaseAuthOptions.MinimumUnknownKeyRefreshInterval"/>.
    /// </summary>
    public async Task<IReadOnlyList<SecurityKey>> GetKeysAsync(string? keyId, CancellationToken cancellationToken)
    {
        var cachedKeys = _cachedKeys;
        if (cachedKeys is not null && !IsExpired(cachedKeys) && cachedKeys.Contains(keyId))
        {
            return cachedKeys.Keys;
        }

        await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedKeys = _cachedKeys;
            if (cachedKeys is not null && !IsExpired(cachedKeys) && !ShouldDownloadForUnknownKey(cachedKeys, keyId))
            {
                return cachedKeys.Keys;
            }

            cachedKeys = await DownloadKeysAsync(cancellationToken).ConfigureAwait(false);
            _cachedKeys = cachedKeys;
            return cachedKeys.Keys;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public void Dispose() => _downloadLock.Dispose();

    private bool IsExpired(CachedKeys cachedKeys) => _timeProvider.GetUtcNow() >= cachedKeys.ExpiresAt;

    private bool ShouldDownloadForUnknownKey(CachedKeys cachedKeys, string? keyId)
    {
        if (cachedKeys.Contains(keyId))
        {
            return false;
        }

        var timeSinceDownload = _timeProvider.GetUtcNow() - cachedKeys.DownloadedAt;
        return timeSinceDownload >= _firebaseOptions.MinimumUnknownKeyRefreshInterval;
    }

    private async Task<CachedKeys> DownloadKeysAsync(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(FirebaseAuthProvider.HttpClientName);
        using var response = await httpClient.GetAsync(_firebaseOptions.PublicKeysUri, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var keySetJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var signingKeys = ReadSigningKeys(keySetJson);

        var downloadedAt = _timeProvider.GetUtcNow();
        var cacheDuration = response.Headers.CacheControl?.MaxAge
            ?? _firebaseOptions.KeysCacheDurationWithoutMaxAge;
        var expiresAt = downloadedAt + cacheDuration;
        return new CachedKeys(signingKeys, downloadedAt, expiresAt);
    }

    private static SecurityKey[] ReadSigningKeys(string keySetJson)
    {
        try
        {
            return new JsonWebKeySet(keySetJson).GetSigningKeys().ToArray();
        }
        catch (ArgumentException exception)
        {
            throw new HttpRequestException("The public key response is not a JSON Web Key Set.", exception);
        }
    }

    private sealed record CachedKeys(
        IReadOnlyList<SecurityKey> Keys,
        DateTimeOffset DownloadedAt,
        DateTimeOffset ExpiresAt)
    {
        public bool Contains(string? keyId) =>
            keyId is null || Keys.Any(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal));
    }
}
