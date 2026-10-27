namespace Pinqponq.Push.DeviceTokens;

/// <summary>
/// Runs after <see cref="IDeviceTokenRegistry.Register"/> saved a row, for what differs per product, for
/// example a topic subscription. Register any number; they run in registration order. An exception from a
/// handler reaches the caller of <c>Register</c>; the row stays saved.
/// </summary>
public interface IDeviceTokenRegisteredHandler
{
    Task Handle(DeviceTokenRegistration registration, CancellationToken cancellationToken = default);
}
