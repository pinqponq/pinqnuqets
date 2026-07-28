using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.Totp;

/// <summary>
/// Default RFC 6238 implementation of <see cref="ITotpService"/>.
/// </summary>
/// <remarks>
/// Use <see cref="ValidateAsync"/> with an application-supplied <see cref="ITotpReplayStore"/>
/// to reject replay of the same code within a time step. Sync <see cref="Validate"/> is
/// crypto-only and does not consult the store.
/// </remarks>
public sealed class TotpService : ITotpService
{
    private static readonly DateTimeOffset UnixEpoch = DateTimeOffset.UnixEpoch;

    private readonly TotpOptions _options;
    private readonly ITotpReplayStore _replayStore;

    /// <summary>Creates the service from configured options and a replay store.</summary>
    public TotpService(IOptions<TotpOptions> options, ITotpReplayStore replayStore)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _replayStore = replayStore ?? throw new ArgumentNullException(nameof(replayStore));
    }

    /// <inheritdoc />
    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(_options.SecretByteLength);
        return Base32.Encode(bytes);
    }

    /// <inheritdoc />
    public string GetProvisioningUri(string secret, string accountName, string? issuer = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentException.ThrowIfNullOrEmpty(accountName);

        var effectiveIssuer = string.IsNullOrEmpty(issuer) ? _options.Issuer : issuer;
        var label = Uri.EscapeDataString($"{effectiveIssuer}:{accountName}");

        var query =
            $"secret={Uri.EscapeDataString(secret)}" +
            $"&issuer={Uri.EscapeDataString(effectiveIssuer)}" +
            $"&algorithm={AlgorithmName(_options.Algorithm)}" +
            $"&digits={_options.Digits}" +
            $"&period={_options.PeriodSeconds}";

        return $"otpauth://totp/{label}?{query}";
    }

    /// <inheritdoc />
    public string ComputeCode(string secret, DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var counter = CounterFor(timestamp ?? DateTimeOffset.UtcNow);
        return ComputeCodeForCounter(Base32.Decode(secret), counter);
    }

    /// <inheritdoc />
    public bool Validate(string secret, string code, DateTimeOffset? timestamp = null) =>
        TryMatchCounter(secret, code, timestamp) is not null;

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(
        string secret,
        string code,
        string subjectKey,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subjectKey);

        var counter = TryMatchCounter(secret, code, timestamp);
        if (counter is null)
        {
            return false;
        }

        return await _replayStore
            .TryAcceptAsync(subjectKey, counter.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private long? TryMatchCounter(string secret, string code, DateTimeOffset? timestamp)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var key = Base32.Decode(secret);
        var current = CounterFor(timestamp ?? DateTimeOffset.UtcNow);
        var window = Math.Max(0, _options.ValidationWindow);

        for (var offset = -window; offset <= window; offset++)
        {
            var candidateCounter = current + offset;
            var candidate = ComputeCodeForCounter(key, candidateCounter);
            if (FixedTimeEquals(candidate, code.Trim()))
            {
                return candidateCounter;
            }
        }

        return null;
    }

    private long CounterFor(DateTimeOffset timestamp)
    {
        var seconds = (long)(timestamp - UnixEpoch).TotalSeconds;
        return seconds / _options.PeriodSeconds;
    }

    private string ComputeCodeForCounter(byte[] key, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[64]; // large enough for SHA-512
        var written = HmacHash(key, counterBytes, hash);
        hash = hash[..written];

        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, _options.Digits);
        var otp = binary % modulo;
        return otp.ToString().PadLeft(_options.Digits, '0');
    }

    private int HmacHash(byte[] key, ReadOnlySpan<byte> message, Span<byte> destination) =>
        _options.Algorithm switch
        {
            TotpAlgorithm.Sha1 => HmacSha1(key, message, destination),
            TotpAlgorithm.Sha256 => HmacSha256(key, message, destination),
            TotpAlgorithm.Sha512 => HmacSha512(key, message, destination),
            _ => throw new InvalidOperationException($"Unsupported algorithm '{_options.Algorithm}'."),
        };

    private static int HmacSha1(byte[] key, ReadOnlySpan<byte> message, Span<byte> destination)
    {
        HMACSHA1.HashData(key, message, destination);
        return HMACSHA1.HashSizeInBytes;
    }

    private static int HmacSha256(byte[] key, ReadOnlySpan<byte> message, Span<byte> destination)
    {
        HMACSHA256.HashData(key, message, destination);
        return HMACSHA256.HashSizeInBytes;
    }

    private static int HmacSha512(byte[] key, ReadOnlySpan<byte> message, Span<byte> destination)
    {
        HMACSHA512.HashData(key, message, destination);
        return HMACSHA512.HashSizeInBytes;
    }

    private static string AlgorithmName(TotpAlgorithm algorithm) => algorithm switch
    {
        TotpAlgorithm.Sha1 => "SHA1",
        TotpAlgorithm.Sha256 => "SHA256",
        TotpAlgorithm.Sha512 => "SHA512",
        _ => throw new InvalidOperationException($"Unsupported algorithm '{algorithm}'."),
    };

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.ASCII.GetBytes(a);
        var bb = Encoding.ASCII.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
