using System.Collections.Concurrent;
using Pinqponq.Identity.Otp;
using Pinqponq.Mail;
using Pinqponq.Sms;

namespace Pinqponq.Identity.Otp.Tests;

internal sealed class InMemoryOtpStore : IOtpStore
{
    private readonly ConcurrentDictionary<string, OtpRecord> _records = new();

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
}

internal sealed class CapturingSmsSender : ISmsSender
{
    public List<SmsMessage> Sent { get; } = [];

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
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
