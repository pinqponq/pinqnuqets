using System.Collections.Concurrent;
using Pinqponq.Identity.Otp;
using Pinqponq.Mail;
using Pinqponq.Sms;

namespace Pinqponq.Identity.Otp.Tests;

internal sealed class InMemoryOtpStore : IOtpStore
{
    private readonly ConcurrentDictionary<string, OtpRecord> _records = new();
    private readonly object _gate = new();

    public int Count => _records.Count;

    public OtpRecord SingleRecord() => _records.Values.Single();

    public Task SaveAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    public Task<OtpRecord?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(key, out var record);
        return Task.FromResult(record);
    }

    public Task UpdateAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(key, out _);
        return Task.CompletedTask;
    }

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

internal sealed class CapturingSmsSender : ISmsSender
{
    public List<SmsMessage> Sent { get; } = [];
    public Exception? ThrowOnSend { get; set; }

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class CapturingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}
