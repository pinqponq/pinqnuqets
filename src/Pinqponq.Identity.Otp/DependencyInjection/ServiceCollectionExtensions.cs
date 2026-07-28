using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        var options = services.AddOptions<OtpOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OtpOptions>, OtpOptionsValidator>());

        services.TryAddSingleton<IOtpSendRateLimiter, AllowAllOtpSendRateLimiter>();
        services.TryAddScoped<IOtpService, OtpService>();
        return services;
    }
}
