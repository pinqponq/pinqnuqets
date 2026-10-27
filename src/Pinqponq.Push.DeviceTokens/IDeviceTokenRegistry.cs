namespace Pinqponq.Push.DeviceTokens;

/// <summary>The store of device tokens clients register for push notifications.</summary>
public interface IDeviceTokenRegistry
{
    /// <summary>
    /// Stores <paramref name="token"/> for <paramref name="userId"/>. A token addresses one device, so a token
    /// that another user registered moves to <paramref name="userId"/>. When the user already has a row for
    /// <paramref name="deviceIdentifier"/>, that row takes the new token and a rotated token does not stay behind.
    /// Handlers registered as <see cref="IDeviceTokenRegisteredHandler"/> run after the row is saved.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The token is empty or longer than <see cref="DeviceTokenOptions.MaxTokenLength"/>, the platform is not in
    /// <see cref="DeviceTokenOptions.Platforms"/>, or the device identifier is longer than
    /// <see cref="DeviceTokenOptions.MaxDeviceIdentifierLength"/>.
    /// </exception>
    Task<DeviceTokenRegistration> Register(
        string userId,
        string token,
        string platform,
        string? deviceIdentifier = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the user's row for <paramref name="token"/>. Returns <see langword="false"/> when there is none.</summary>
    Task<bool> Unregister(string userId, string token, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetTokens(string userId, CancellationToken cancellationToken = default);

    /// <summary>Removes the rows of these tokens whoever owns them, for example tokens the push provider rejected.</summary>
    /// <returns>The number of rows removed.</returns>
    Task<int> RemoveTokens(IReadOnlyCollection<string> tokens, CancellationToken cancellationToken = default);
}
