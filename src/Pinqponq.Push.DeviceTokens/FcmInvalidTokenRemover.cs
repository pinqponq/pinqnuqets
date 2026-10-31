using Pinqponq.Push.Fcm;

namespace Pinqponq.Push.DeviceTokens;

internal sealed class FcmInvalidTokenRemover(IDeviceTokenRegistry registry) : IFcmInvalidTokenHandler
{
    public async Task HandleInvalidTokens(IReadOnlyList<string> invalidTokens, CancellationToken cancellationToken = default) =>
        await registry.RemoveTokens(invalidTokens, cancellationToken).ConfigureAwait(false);
}
