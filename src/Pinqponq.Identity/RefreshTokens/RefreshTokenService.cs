using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.RefreshTokens;

/// <summary>
/// Default <see cref="IRefreshTokenService"/>. Generates cryptographically random tokens,
/// stores only their SHA-256 hash, and implements rotate/revoke with a replacement chain
/// for reuse detection.
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenStore _store;
    private readonly RefreshTokenOptions _options;

    /// <summary>Creates the service from a store and configured options.</summary>
    public RefreshTokenService(IRefreshTokenStore store, IOptions<RefreshTokenOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<RefreshTokenResult> IssueAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        return await CreateAndStoreAsync(subject, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RefreshTokenResult> RotateAsync(
        string presentedToken,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetActiveOrThrowAsync(presentedToken, cancellationToken).ConfigureAwait(false);

        var replacement = await CreateAndStoreAsync(existing.Subject, cancellationToken).ConfigureAwait(false);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenHash = replacement.Descriptor.TokenHash;
        await _store.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);

        return replacement;
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        string presentedToken,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetActiveOrThrowAsync(presentedToken, cancellationToken).ConfigureAwait(false);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RefreshToken> GetActiveOrThrowAsync(
        string presentedToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);

        var hash = Hash(presentedToken);
        var token = await _store.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);

        if (token is null || !token.IsActive(DateTimeOffset.UtcNow))
        {
            throw new InvalidRefreshTokenException();
        }

        return token;
    }

    private async Task<RefreshTokenResult> CreateAndStoreAsync(
        string subject,
        CancellationToken cancellationToken)
    {
        var raw = GenerateRawToken(_options.TokenByteLength);
        var now = DateTimeOffset.UtcNow;

        var entity = new RefreshToken
        {
            TokenHash = Hash(raw),
            Subject = subject,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.Lifetime),
        };

        await _store.AddAsync(entity, cancellationToken).ConfigureAwait(false);

        return new RefreshTokenResult { Token = raw, Descriptor = entity };
    }

    private static string GenerateRawToken(int byteLength)
    {
        if (byteLength <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(RefreshTokenOptions.TokenByteLength)} must be greater than zero.");
        }

        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64UrlEncode(bytes);
    }

    private static string Hash(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
