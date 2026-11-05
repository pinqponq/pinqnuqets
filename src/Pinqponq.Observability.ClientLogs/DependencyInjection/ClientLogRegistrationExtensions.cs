using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Observability.ClientLogs.DependencyInjection;

/// <summary>
/// Registration and endpoint helpers for the client log relay.
/// </summary>
public static class ClientLogRegistrationExtensions
{
    /// <summary>
    /// Binds <see cref="ClientLogOptions"/> to <paramref name="clientLogSection"/> (then
    /// <paramref name="configure"/>), validates them at startup and registers the relay. Call
    /// <c>AddPinqponqObservability</c> too: the relay writes through its Pinqloq client and reads
    /// <see cref="ObservabilityOptions.DeviceIdentifierHeader"/> and <see cref="ObservabilityOptions.RedactedKeys"/>.
    /// </summary>
    public static IServiceCollection AddPinqponqClientLogs(
        this IServiceCollection services,
        IConfiguration clientLogSection,
        Action<ClientLogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientLogSection);

        var optionsBuilder = services.AddOptions<ClientLogOptions>()
            .Bind(clientLogSection)
            .ValidateOnStart();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClientLogOptions>, ClientLogOptionsValidator>());
        services.TryAddSingleton<ClientLogRelay>();
        return services;
    }

    /// <summary>
    /// Maps a POST endpoint that takes one client log per request (the pinqloq-kmp <c>endpoint</c>) and
    /// answers 204. Authorization and rate limiting are set by the caller on the returned builder.
    /// </summary>
    public static RouteHandlerBuilder MapPinqponqClientLogs(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapPost(pattern, RelayEntry);
    }

    /// <summary>
    /// Maps a POST endpoint that takes a <c>{"logs": [...]}</c> batch (the pinqloq-kmp <c>batchEndpoint</c>)
    /// and answers 204. Authorization and rate limiting are set by the caller on the returned builder.
    /// </summary>
    public static RouteHandlerBuilder MapPinqponqClientLogBatch(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapPost(pattern, RelayBatch);
    }

    private static async Task<IResult> RelayEntry(HttpContext httpContext, CancellationToken cancellationToken)
    {
        await ResolveRelay(httpContext).RelayEntry(httpContext, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RelayBatch(HttpContext httpContext, CancellationToken cancellationToken)
    {
        await ResolveRelay(httpContext).RelayBatch(httpContext, cancellationToken);
        return Results.NoContent();
    }

    private static ClientLogRelay ResolveRelay(HttpContext httpContext) =>
        httpContext.RequestServices.GetService<ClientLogRelay>()
        ?? throw new InvalidOperationException(
            $"The client log endpoints need {nameof(AddPinqponqClientLogs)} in the service registration.");
}
