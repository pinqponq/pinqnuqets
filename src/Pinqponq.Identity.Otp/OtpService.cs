using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pinqponq.Mail;
using Pinqponq.Sms;

namespace Pinqponq.Identity.Otp;

/// <summary>
/// Default <see cref="IOtpService"/>. Generates cryptographically random numeric codes,
/// stores only their hash, routes delivery to SMS or email, and enforces TTL and attempt
/// limits on verification.
/// </summary>
public sealed class OtpService : IOtpService
{
    private readonly IOtpStore _store;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;
    private readonly OtpOptions _options;

    /// <summary>Creates the service from its store, channel senders and options.</summary>
    public OtpService(
        IOtpStore store,
        ISmsSender smsSender,
        IEmailSender emailSender,
        IOptions<OtpOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _smsSender = smsSender ?? throw new ArgumentNullException(nameof(smsSender));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task GenerateAndSendAsync(
        string recipient,
        OtpChannel channel = OtpChannel.Auto,
        string purpose = "default",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        var code = GenerateNumericCode(_options.CodeLength);
        var now = DateTimeOffset.UtcNow;

        var record = new OtpRecord
        {
            Key = BuildKey(recipient, purpose),
            CodeHash = Hash(code, recipient),
            Recipient = recipient,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.Ttl),
            Attempts = 0,
        };

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await SendAsync(recipient, code, channel, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OtpVerifyStatus> VerifyAsync(
        string recipient,
        string code,
        string purpose = "default",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        var key = BuildKey(recipient, purpose);
        var record = await _store.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return OtpVerifyStatus.NotFound;
        }

        if (DateTimeOffset.UtcNow >= record.ExpiresAt)
        {
            await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return OtpVerifyStatus.Expired;
        }

        if (record.Attempts >= _options.MaxAttempts)
        {
            await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return OtpVerifyStatus.TooManyAttempts;
        }

        if (!FixedTimeEquals(record.CodeHash, Hash(code, recipient)))
        {
            record.Attempts++;
            await _store.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
            return OtpVerifyStatus.Mismatch;
        }

        await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        return OtpVerifyStatus.Success;
    }

    private Task SendAsync(string recipient, string code, OtpChannel channel, CancellationToken cancellationToken)
    {
        var resolved = channel == OtpChannel.Auto
            ? (IsEmail(recipient) ? OtpChannel.Email : OtpChannel.Sms)
            : channel;

        return resolved == OtpChannel.Email
            ? _emailSender.SendAsync(
                new EmailMessage
                {
                    To = recipient,
                    Subject = Format(_options.EmailSubjectTemplate, code),
                    Body = Format(_options.EmailBodyTemplate, code),
                },
                cancellationToken)
            : _smsSender.SendAsync(
                new SmsMessage { To = recipient, Text = Format(_options.SmsTemplate, code) },
                cancellationToken);
    }

    private static string BuildKey(string recipient, string purpose) =>
        $"otp:{purpose}:{recipient.Trim().ToLowerInvariant()}";

    private static bool IsEmail(string recipient) => recipient.Contains('@', StringComparison.Ordinal);

    private static string Format(string template, string code) =>
        string.Format(CultureInfo.InvariantCulture, template, code);

    private static string GenerateNumericCode(int length)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(RandomNumberGenerator.GetInt32(0, 10));
        }

        return builder.ToString();
    }

    private static string Hash(string code, string recipient)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{recipient.Trim().ToLowerInvariant()}:{code}"));
        return Convert.ToHexString(digest);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
