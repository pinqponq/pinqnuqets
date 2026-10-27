using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Pinqponq.Push.DeviceTokens;

/// <summary>Request body of the register endpoint.</summary>
public sealed record RegisterDeviceTokenRequest(string Token, string Platform, string? DeviceIdentifier = null);

/// <summary>Request body of the unregister endpoint.</summary>
public sealed record UnregisterDeviceTokenRequest(string Token);

/// <summary>Optional endpoints for the device token store. A product can write its own instead.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps register (<c>POST</c>) and unregister (<c>DELETE</c>) for the signed-in user. The group requires
    /// authorization; the returned builder lets the application add a policy or metadata. A request the registry
    /// rejects as invalid gets 400, a request without a user id claim gets 401.
    /// </summary>
    public static RouteGroupBuilder MapPinqponqDeviceTokenEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<DeviceTokenEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new DeviceTokenEndpointOptions();
        configure?.Invoke(options);

        var group = endpoints.MapGroup(options.Route).RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] RegisterDeviceTokenRequest request,
            HttpContext context,
            IDeviceTokenRegistry registry,
            CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirst(options.UserIdClaimType)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            try
            {
                await registry.Register(userId, request.Token, request.Platform, request.DeviceIdentifier, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.NoContent();
        });

        group.MapDelete("/", async (
            [FromBody] UnregisterDeviceTokenRequest request,
            HttpContext context,
            IDeviceTokenRegistry registry,
            CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirst(options.UserIdClaimType)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.Token))
            {
                return Results.Problem(detail: "The token is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            await registry.Unregister(userId, request.Token, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });

        return group;
    }
}
