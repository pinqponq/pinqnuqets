using System.Collections.Concurrent;
using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.Tests.RefreshTokens;

/// <summary>
/// Minimal in-memory <see cref="IRefreshTokenStore"/> for tests.
/// </summary>
internal sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshToken> _byHash = new();
    private readonly object _gate = new();

    /// <summary>
    /// When set, <see cref="CompleteRotationAsync"/> throws after optionally writing
    /// the replacement but before linking — used to simulate crash windows.
    /// </summary>
    public Exception? ThrowOnCompleteRotation { get; set; }

    /// <summary>
    /// When <see langword="true"/>, fail after adding the replacement but before linking
    /// <see cref="RefreshToken.ReplacedByTokenHash"/> (partial write simulation).
    /// </summary>
    public bool FailAfterAddBeforeLink { get; set; }

    public int Count => _byHash.Count;

    public int ActiveCount
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            return _byHash.Values.Count(t => t.IsActive(now));
        }
    }

    public Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _byHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        _byHash.TryGetValue(tokenHash, out var token);
        return Task.FromResult(token);
    }

    public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _byHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<bool> TryRevokeActiveAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_byHash.TryGetValue(tokenHash, out var token) || !token.IsActive(revokedAt))
            {
                return Task.FromResult(false);
            }

            token.RevokedAt = revokedAt;
            return Task.FromResult(true);
        }
    }

    public Task CompleteRotationAsync(
        string revokedTokenHash,
        RefreshToken replacement,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_byHash.TryGetValue(revokedTokenHash, out var revoked))
            {
                throw new InvalidOperationException("Revoked token was not found.");
            }

            if (ThrowOnCompleteRotation is not null && !FailAfterAddBeforeLink)
            {
                throw ThrowOnCompleteRotation;
            }

            _byHash[replacement.TokenHash] = replacement;

            if (FailAfterAddBeforeLink)
            {
                throw ThrowOnCompleteRotation
                    ?? new InvalidOperationException("Simulated crash after add before link.");
            }

            revoked.ReplacedByTokenHash = replacement.TokenHash;
            return Task.CompletedTask;
        }
    }

    public Task RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            foreach (var token in _byHash.Values)
            {
                if (string.Equals(token.Subject, subject, StringComparison.Ordinal)
                    && token.RevokedAt is null)
                {
                    token.RevokedAt = now;
                }
            }
        }

        return Task.CompletedTask;
    }
}
