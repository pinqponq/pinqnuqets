using System.Collections.Concurrent;
using Pinqponq.Auth.Totp;
using Pinqponq.Identity.Jwt;
using Pinqponq.Identity.Otp;

namespace Pinqponq.Playground.Scenarios.Support;

/// <summary>In-memory <see cref="IAccessTokenRevocationStore"/> for jti revoke demos.</summary>
public sealed class InMemoryAccessTokenRevocationStore : IAccessTokenRevocationStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new(StringComparer.Ordinal);

    /// <summary>Currently revoked JTIs still within their expiry window.</summary>
    public IReadOnlyCollection<string> RevokedJtis =>
        [.. _revoked.Where(pair => pair.Value > DateTimeOffset.UtcNow).Select(pair => pair.Key)];

    /// <inheritdoc />
    public Task RevokeAsync(
        string jti,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        _revoked[jti] = expiresAt;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _revoked.TryGetValue(jti, out var expiresAt) && expiresAt > DateTimeOffset.UtcNow);
}

/// <summary>In-memory <see cref="ITotpReplayStore"/> for ValidateAsync demos.</summary>
public sealed class InMemoryTotpReplayStore : ITotpReplayStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _accepted = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> TryAcceptAsync(
        string subjectKey,
        long counter,
        CancellationToken cancellationToken = default)
    {
        var counters = _accepted.GetOrAdd(subjectKey, _ => new ConcurrentDictionary<long, byte>());
        return Task.FromResult(counters.TryAdd(counter, 0));
    }
}

/// <summary>
/// Records send times so OTP rate-limit scenarios can exercise
/// <see cref="IOtpSendRateLimiter"/> without Redis.
/// </summary>
public sealed class InMemoryOtpSendRateLimiter : IOtpSendRateLimiter
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSend = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> TryAcquireAsync(
        string key,
        TimeSpan minInterval,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        while (true)
        {
            if (!_lastSend.TryGetValue(key, out var previous))
            {
                if (_lastSend.TryAdd(key, now))
                {
                    return Task.FromResult(true);
                }

                continue;
            }

            if (now - previous < minInterval)
            {
                return Task.FromResult(false);
            }

            if (_lastSend.TryUpdate(key, now, previous))
            {
                return Task.FromResult(true);
            }
        }
    }
}

/// <summary>Shared OTP playground defaults matching 0.2.x required options.</summary>
internal static class OtpPlayground
{
    /// <summary>≥32 character pepper used by every OTP scenario host.</summary>
    public const string HashPepper = "playground-otp-pepper-0123456789abcdef";

    public static void ApplyDefaults(OtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.HashPepper = HashPepper;
    }
}
