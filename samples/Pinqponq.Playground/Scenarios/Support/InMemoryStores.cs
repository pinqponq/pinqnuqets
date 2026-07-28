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
}

/// <summary>In-memory <see cref="IOtpStore"/>, the counterpart the OTP package expects.</summary>
public sealed class InMemoryOtpStore : IOtpStore
{
    private readonly ConcurrentDictionary<string, OtpRecord> _records = new(StringComparer.Ordinal);

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
}
