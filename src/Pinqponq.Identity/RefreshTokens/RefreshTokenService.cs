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
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);

        var hash = Hash(presentedToken);
        var now = DateTimeOffset.UtcNow;

        if (!await _store.TryRevokeActiveAsync(hash, now, cancellationToken).ConfigureAwait(false))
        {
            await RejectInactiveOrUnknownAsync(hash, now, cancellationToken).ConfigureAwait(false);
        }

        var existing = await _store.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidRefreshTokenException();

        var raw = GenerateRawToken(_options.TokenByteLength);
        var replacementEntity = new RefreshToken
        {
            TokenHash = Hash(raw),
            Subject = existing.Subject,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.Lifetime),
        };

        try
        {
            await _store.CompleteRotationAsync(hash, replacementEntity, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await _store.RevokeAllForSubjectAsync(existing.Subject, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        return new RefreshTokenResult { Token = raw, Descriptor = replacementEntity };
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        string presentedToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);

        var hash = Hash(presentedToken);
        var now = DateTimeOffset.UtcNow;

        if (!await _store.TryRevokeActiveAsync(hash, now, cancellationToken).ConfigureAwait(false))
        {
            await RejectInactiveOrUnknownAsync(hash, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RejectInactiveOrUnknownAsync(
        string hash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = await _store.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);

        // Reuse of a rotated token after the grace window → compromise: revoke family.
        // Within the grace window, concurrent double-submit must not kill the replacement.
        if (token is not null
            && token.RevokedAt is not null
            && !string.IsNullOrEmpty(token.ReplacedByTokenHash)
            && now - token.RevokedAt.Value > _options.ReuseDetectionGrace)
        {
            await _store.RevokeAllForSubjectAsync(token.Subject, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidRefreshTokenException();
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
