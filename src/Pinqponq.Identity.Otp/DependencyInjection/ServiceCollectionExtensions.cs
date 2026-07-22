using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pinqponq.Identity.Otp.DependencyInjection;

/// <summary>
/// Registration helpers for OTP.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IOtpService"/>. The consumer must also register
    /// <see cref="IOtpStore"/> and the channel senders (<c>AddPinqponqSms</c> /
    /// <c>AddPinqponqMail</c>).
    /// </summary>
    public static IServiceCollection AddPinqponqOtp(
        this IServiceCollection services,
        Action<OtpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddScoped<IOtpService, OtpService>();
        return services;
    }
}
