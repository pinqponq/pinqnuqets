using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.Rooms;
using Pinqponq.LiveKit.Server.Webhooks;

namespace Pinqponq.LiveKit.Server.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RoomServiceClient"/>, <see cref="AccessTokenIssuer"/> and <see cref="WebhookReceiver"/>.
    /// Chain <see cref="LiveKitServerBuilder.AddStaticCredentials"/> or <see cref="LiveKitServerBuilder.AddCredentialsProvider{T}"/>.
    /// </summary>
    public static LiveKitServerBuilder AddPinqponqLiveKitServer(this IServiceCollection services, Action<LiveKitServerOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddOptions<LiveKitServerOptions>()
            .Configure(configureOptions)
            .Validate(serverOptions => LiveKitServerUrl.IsValid(serverOptions.Url), "LiveKit Url must be an absolute http, https, ws or wss URL.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(nameof(RoomServiceClient)).AddTypedClient((httpClient, serviceProvider) => new RoomServiceClient(
            httpClient,
            serviceProvider.GetRequiredService<IOptions<LiveKitServerOptions>>().Value,
            serviceProvider.GetRequiredService<ILiveKitCredentialsProvider>(),
            serviceProvider.GetRequiredService<TimeProvider>()));

        services.TryAddTransient(serviceProvider => new AccessTokenIssuer(
            serviceProvider.GetRequiredService<ILiveKitCredentialsProvider>(),
            serviceProvider.GetRequiredService<TimeProvider>()));

        services.TryAddTransient(serviceProvider => new WebhookReceiver(
            serviceProvider.GetRequiredService<ILiveKitCredentialsProvider>(),
            serviceProvider.GetRequiredService<TimeProvider>()));

        return new LiveKitServerBuilder(services);
    }
}
