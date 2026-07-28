using System.Collections.Concurrent;
using Pinqponq.Identity.Otp;
using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Playground.Scenarios.Support;

/// <summary>
/// The refresh-token storage the package deliberately does not ship. Kept per run so the
/// console can show exactly what was persisted — notably that only the hash is.
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshToken> _tokens = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Everything currently stored, for display.</summary>
    public IReadOnlyCollection<RefreshToken> All => [.. _tokens.Values];

    /// <inheritdoc />
    public Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        _tokens[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tokens.TryGetValue(tokenHash, out var token) ? token : null);

    /// <inheritdoc />
    public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        _tokens[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryRevokeActiveAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(tokenHash, out var token) || !token.IsActive(revokedAt))
            {
                return Task.FromResult(false);
            }

            token.RevokedAt = revokedAt;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task CompleteRotationAsync(
        string revokedTokenHash,
        RefreshToken replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (_gate)
        {
            if (!_tokens.TryGetValue(revokedTokenHash, out var revoked))
            {
                throw new InvalidOperationException("Revoked token was not found.");
            }

            _tokens[replacement.TokenHash] = replacement;
            revoked.ReplacedByTokenHash = replacement.TokenHash;
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            foreach (var token in _tokens.Values)
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

/// <summary>In-memory <see cref="IOtpStore"/>, the counterpart the OTP package expects.</summary>
public sealed class InMemoryOtpStore : IOtpStore
{
    private readonly ConcurrentDictionary<string, OtpRecord> _records = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Keys currently held, so the console can show the key format the package uses.</summary>
    public IReadOnlyCollection<string> Keys => [.. _records.Keys];

    /// <summary>Records currently held, for display.</summary>
    public IReadOnlyCollection<OtpRecord> All => [.. _records.Values];

    /// <inheritdoc />
    public Task SaveAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OtpRecord?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.TryGetValue(key, out var record) ? record : null);

    /// <inheritdoc />
    public Task UpdateAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryRemoveAsync(
        string key,
        string expectedCodeHash,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var record)
                || !string.Equals(record.CodeHash, expectedCodeHash, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _records.TryRemove(key, out _);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<OtpVerifyStatus> TryConsumeAsync(
        string key,
        string codeHash,
        int maxAttempts,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                return Task.FromResult(OtpVerifyStatus.NotFound);
            }

            if (utcNow >= record.ExpiresAt)
            {
                _records.TryRemove(key, out _);
                return Task.FromResult(OtpVerifyStatus.Expired);
            }

            if (record.Attempts >= maxAttempts)
            {
                _records.TryRemove(key, out _);
                return Task.FromResult(OtpVerifyStatus.TooManyAttempts);
            }

            if (!string.Equals(record.CodeHash, codeHash, StringComparison.Ordinal))
            {
                record.Attempts++;
                _records[key] = record;
                return Task.FromResult(OtpVerifyStatus.Mismatch);
            }

            _records.TryRemove(key, out _);
            return Task.FromResult(OtpVerifyStatus.Success);
        }
    }
}
