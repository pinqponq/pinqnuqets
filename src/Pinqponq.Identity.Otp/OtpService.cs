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
    private readonly ISmsSender? _smsSender;
    private readonly IEmailSender? _emailSender;
    private readonly IOtpSendRateLimiter _rateLimiter;
    private readonly OtpOptions _options;

    /// <summary>Creates the service from its store, channel senders and options.</summary>
    /// <remarks>
    /// A sender is only needed for the channel the application actually uses, so either
    /// one may be <see langword="null"/>. Routing a code to a channel whose sender is
    /// missing throws at that point rather than at registration.
    /// </remarks>
    public OtpService(
        IOtpStore store,
        ISmsSender? smsSender,
        IEmailSender? emailSender,
        IOtpSendRateLimiter rateLimiter,
        IOptions<OtpOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _smsSender = smsSender;
        _emailSender = emailSender;
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
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
        var key = BuildKey(recipient, purpose);

        if (_options.MinSendInterval > TimeSpan.Zero)
        {
            var acquired = await _rateLimiter
                .TryAcquireAsync(key, _options.MinSendInterval, cancellationToken)
                .ConfigureAwait(false);
            if (!acquired)
            {
                throw new OtpSendRateLimitedException();
            }
        }

        var record = new OtpRecord
        {
            Key = key,
            CodeHash = Hash(code, recipient),
            Recipient = recipient,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.Ttl),
            Attempts = 0,
        };

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        try
        {
            await SendAsync(recipient, code, channel, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Delivery failed — remove only this code (CAS) so a concurrent generate is kept.
            // Use CancellationToken.None so a cancelled send still rolls back.
            await _store.TryRemoveAsync(key, record.CodeHash, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<OtpVerifyStatus> VerifyAsync(
        string recipient,
        string code,
        string purpose = "default",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        var key = BuildKey(recipient, purpose);
        // Whitespace/null codes never match a real HMAC/SHA digest.
        var codeHash = string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : Hash(code.Trim(), recipient);

        return _store.TryConsumeAsync(
            key,
            codeHash,
            _options.MaxAttempts,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private Task SendAsync(string recipient, string code, OtpChannel channel, CancellationToken cancellationToken)
    {
        var resolved = channel == OtpChannel.Auto
            ? (IsEmail(recipient) ? OtpChannel.Email : OtpChannel.Sms)
            : channel;

        return resolved == OtpChannel.Email
            ? Required(_emailSender, nameof(IEmailSender), "AddPinqponqMail").SendAsync(
                new EmailMessage
                {
                    To = recipient,
                    Subject = Format(_options.EmailSubjectTemplate, code),
                    Body = Format(_options.EmailBodyTemplate, code),
                },
                cancellationToken)
            : Required(_smsSender, nameof(ISmsSender), "AddPinqponqSms").SendAsync(
                new SmsMessage { To = recipient, Text = Format(_options.SmsTemplate, code) },
                cancellationToken);
    }

    private static TSender Required<TSender>(TSender? sender, string contract, string registration)
        where TSender : class =>
        sender ?? throw new InvalidOperationException(
            $"Delivering this code needs an {contract}, which is not registered. "
            + $"Call {registration} during startup, or route the code to the other channel.");

    private string BuildKey(string recipient, string purpose)
    {
        var material = $"{purpose}\0{recipient.Trim().ToLowerInvariant()}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "otp:" + Convert.ToHexString(digest);
    }

    private string Hash(string code, string recipient)
    {
        var normalized = $"{recipient.Trim().ToLowerInvariant()}:{code}";
        var key = Encoding.UTF8.GetBytes(_options.HashPepper);
        var data = Encoding.UTF8.GetBytes(normalized);
        var digest = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(digest);
    }

    private static bool IsEmail(string recipient) => recipient.Contains('@', StringComparison.Ordinal);

    private static string Format(string template, string code) =>
        string.Format(CultureInfo.InvariantCulture, template, code);

    private static string GenerateNumericCode(int length)
    {
        if (length <= 0)
        {
            throw new InvalidOperationException($"{nameof(OtpOptions.CodeLength)} must be greater than zero.");
        }

        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(RandomNumberGenerator.GetInt32(0, 10));
        }

        return builder.ToString();
    }
}
