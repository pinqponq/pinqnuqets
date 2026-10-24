using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

namespace Pinqponq.OpenApi;

/// <summary>Endpoint mapping for Pinqponq OpenAPI.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the OpenAPI document and the Scalar reference in the environments allowed by
    /// <see cref="PinqponqOpenApiOptions.ExposeIn"/>. In any other environment nothing is mapped and the routes
    /// answer 404. Returns a group, so a product can add its own conventions, for example rate limiting.
    /// </summary>
    public static RouteGroupBuilder MapPinqponqOpenApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<PinqponqOpenApiOptions>>().Value;
        var hostEnvironment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var group = endpoints.MapGroup(string.Empty);

        if (!IsExposed(options.ExposeIn, hostEnvironment))
        {
            return group;
        }

        group.MapOpenApi(options.OpenApiRoutePattern);
        group.MapScalarApiReference(options.ScalarRoutePrefix, scalarOptions =>
        {
            scalarOptions.OpenApiRoutePattern = options.OpenApiRoutePattern;
        });

        if (options.AuthorizationPolicy is not null)
        {
            group.RequireAuthorization(options.AuthorizationPolicy);
        }

        return group;
    }

    private static bool IsExposed(PinqponqOpenApiExposure exposure, IHostEnvironment hostEnvironment) => exposure switch
    {
        PinqponqOpenApiExposure.Always => true,
        PinqponqOpenApiExposure.DevelopmentOnly => hostEnvironment.IsDevelopment(),
        _ => false,
    };
}
