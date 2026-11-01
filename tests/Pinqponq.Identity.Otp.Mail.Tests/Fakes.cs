using Pinqponq.Mail;

namespace Pinqponq.Identity.Otp.Mail.Tests;

internal sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Messages { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FixedLanguageResolver(string? language) : IOtpMailLanguageResolver
{
    public List<string> Recipients { get; } = [];

    public Task<string?> ResolveLanguageAsync(string recipient, CancellationToken cancellationToken = default)
    {
        Recipients.Add(recipient);
        return Task.FromResult(language);
    }
}

internal sealed class RecordingTemplate : IOtpMailTemplate
{
    public List<OtpMailModel> Models { get; } = [];

    public string Render(OtpMailModel model)
    {
        Models.Add(model);
        return "<custom>" + model.Code + "</custom>";
    }
}

internal sealed class InMemoryOtpStore : IOtpStore
{
    private readonly Dictionary<string, OtpRecord> _records = new();

    public Task SaveAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    public Task<OtpRecord?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.GetValueOrDefault(key));

    public Task UpdateAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Key] = record;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _records.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> TryRemoveAsync(string key, string expectedCodeHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.Remove(key));

    public Task<OtpVerifyStatus> TryConsumeAsync(
        string key,
        string codeHash,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OtpVerifyStatus.NotFound);
}
