using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.Messaging.RabbitMq.DependencyInjection;

/// <summary>
/// Registration helpers for RabbitMQ messaging.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared connection and <see cref="IMessagePublisher"/>.
    /// </summary>
    public static IServiceCollection AddPinqponqRabbitMq(
        this IServiceCollection services,
        Action<RabbitMqOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.TryAddSingleton<IMessagePublisher, RabbitMqPublisher>();
        return services;
    }

    /// <summary>
    /// Registers a background consumer that dispatches messages to
    /// <typeparamref name="THandler"/>.
    /// </summary>
    public static IServiceCollection AddRabbitMqConsumer<THandler>(
        this IServiceCollection services,
        Action<RabbitMqConsumerOptions> configure)
        where THandler : class, IMessageHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var consumerOptions = new RabbitMqConsumerOptions { Queue = string.Empty };
        configure(consumerOptions);
        if (string.IsNullOrWhiteSpace(consumerOptions.Queue))
        {
            throw new InvalidOperationException("RabbitMqConsumerOptions.Queue is required.");
        }

        services.TryAddScoped<THandler>();
        services.AddSingleton<IHostedService>(sp => new RabbitMqConsumerService<THandler>(
            sp.GetRequiredService<IRabbitMqConnection>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
            consumerOptions,
            sp.GetRequiredService<ILogger<RabbitMqConsumerService<THandler>>>()));

        return services;
    }
}
