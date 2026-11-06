using System.Security.Claims;

namespace Pinqponq.Push.DeviceTokens;

/// <summary>Settings for <see cref="EndpointRouteBuilderExtensions.MapPinqponqDeviceTokenEndpoints"/>.</summary>
public sealed class DeviceTokenEndpointOptions
{
    /// <summary>Route of the endpoints: <c>POST</c> registers, <c>DELETE</c> unregisters. Defaults to <c>/device-tokens</c>.</summary>
    public string Route { get; set; } = "/device-tokens";

    /// <summary>Claim that holds the user id. Defaults to <see cref="ClaimTypes.NameIdentifier"/>.</summary>
    public string UserIdClaimType { get; set; } = ClaimTypes.NameIdentifier;
}
