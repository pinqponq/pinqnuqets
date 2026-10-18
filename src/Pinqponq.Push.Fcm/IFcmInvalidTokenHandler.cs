namespace Pinqponq.Push.Fcm;

/// <summary>
/// Receives the device tokens FCM reported as invalid after a send, so the application deletes them from
/// its own device store. The default implementation does nothing; register your own to replace it.
/// </summary>
public interface IFcmInvalidTokenHandler
{
    Task HandleInvalidTokens(IReadOnlyList<string> invalidTokens, CancellationToken cancellationToken = default);
}

internal sealed class NullFcmInvalidTokenHandler : IFcmInvalidTokenHandler
{
    public Task HandleInvalidTokens(IReadOnlyList<string> invalidTokens, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
