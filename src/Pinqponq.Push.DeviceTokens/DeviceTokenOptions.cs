namespace Pinqponq.Push.DeviceTokens;

/// <summary>Settings for <see cref="IDeviceTokenRegistry"/>.</summary>
public sealed class DeviceTokenOptions
{
    /// <summary>Longest token <see cref="IDeviceTokenRegistry.Register"/> accepts. Defaults to 4096.</summary>
    public int MaxTokenLength { get; set; } = 4096;

    /// <summary>
    /// Longest device identifier <see cref="IDeviceTokenRegistry.Register"/> accepts. Defaults to 256.
    /// </summary>
    public int MaxDeviceIdentifierLength { get; set; } = 256;

    /// <summary>
    /// Platform names <see cref="IDeviceTokenRegistry.Register"/> accepts, compared without regard to case.
    /// The stored value is the spelling listed here. Defaults to <c>android</c>, <c>ios</c> and <c>web</c>.
    /// </summary>
    public IList<string> Platforms { get; set; } = ["android", "ios", "web"];

    /// <summary>
    /// When <see langword="true"/> (the default), tokens that FCM reports as invalid are removed from the store
    /// by an <c>IFcmInvalidTokenHandler</c> that <c>AddPinqponqDeviceTokens</c> registers. Register your own
    /// handler after it to replace it.
    /// </summary>
    public bool RemoveTokensFcmReportsInvalid { get; set; } = true;
}
