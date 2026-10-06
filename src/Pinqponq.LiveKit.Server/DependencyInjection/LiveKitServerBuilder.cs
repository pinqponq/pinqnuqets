using Microsoft.Extensions.DependencyInjection;

namespace Pinqponq.LiveKit.Server.DependencyInjection;

public sealed class LiveKitServerBuilder
{
    public IServiceCollection Services { get; }

    internal LiveKitServerBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public LiveKitServerBuilder AddStaticCredentials(string apiKey, string apiSecret)
    {
        var credentials = new LiveKitCredentials(apiKey, apiSecret);
        Services.AddSingleton<ILiveKitCredentialsProvider>(new StaticLiveKitCredentialsProvider(credentials));
        return this;
    }

    public LiveKitServerBuilder AddCredentialsProvider<TCredentialsProvider>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TCredentialsProvider : class, ILiveKitCredentialsProvider
    {
        Services.Add(new ServiceDescriptor(typeof(ILiveKitCredentialsProvider), typeof(TCredentialsProvider), lifetime));
        return this;
    }
}
