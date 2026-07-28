using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Sms.DependencyInjection;

/// <summary>
/// Registration helpers for SMS sending.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISmsSender"/> (NetGSM) and its named <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddPinqponqSms(
        this IServiceCollection services,
        Action<SmsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<SmsOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SmsOptions>, SmsOptionsValidator>());

        services.AddHttpClient(NetGsmSmsSender.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<SmsOptions>>().Value;
                client.Timeout = options.HttpTimeout;
            });
        services.TryAddScoped<ISmsSender, NetGsmSmsSender>();
        return services;
    }
}
