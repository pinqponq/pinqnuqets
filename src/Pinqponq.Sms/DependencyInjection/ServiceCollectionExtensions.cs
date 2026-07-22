using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.Configure(configure);
        services.AddHttpClient(NetGsmSmsSender.HttpClientName);
        services.TryAddScoped<ISmsSender, NetGsmSmsSender>();
        return services;
    }
}
