namespace Pinqponq.Push.DeviceTokens;

/// <summary>What <see cref="IDeviceTokenRegistry.Register"/> stored.</summary>
/// <param name="DeviceToken">The saved row.</param>
/// <param name="IsNewRow">The registration inserted a row instead of updating or moving one.</param>
/// <param name="PreviousUserId">The user the row belonged to before, when it was updated or moved.</param>
/// <param name="PreviousToken">The token the row held before, when it was updated or moved.</param>
public sealed record DeviceTokenRegistration(
    DeviceToken DeviceToken,
    bool IsNewRow,
    string? PreviousUserId,
    string? PreviousToken)
{
    /// <summary>The registration replaced the token of a known device.</summary>
    public bool IsTokenRotation => PreviousToken is not null && PreviousToken != DeviceToken.Token;
}
