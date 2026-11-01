using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.Otp.Mail.DependencyInjection;

/// <summary>Registration helpers for the OTP code mail.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Makes <see cref="IOtpService"/> send its email codes as the branded mail of this package:
    /// replaces the registered <see cref="IOtpMessageComposer"/> with
    /// <see cref="OtpMailMessageComposer"/>, registers the default <see cref="IOtpMailTemplate"/>
    /// and <see cref="IOtpMailLanguageResolver"/>, and validates the options on startup. Needs
    /// <c>AddPinqponqOtp</c> (which reads the code lifetime) and <c>AddPinqponqMail</c> (which
    /// sends the mail). The call order does not matter.
    /// </summary>
    public static IServiceCollection AddPinqponqOtpMail(
        this IServiceCollection services,
        Action<OtpMailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<OtpMailOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OtpMailOptions>, OtpMailOptionsValidator>());

        services.TryAddSingleton<IOtpMailTemplate, OtpMailTemplate>();
        services.TryAddScoped<IOtpMailLanguageResolver, DefaultOtpMailLanguageResolver>();

        // Scoped because a product's language resolver usually reads the user from a scoped store.
        services.Replace(ServiceDescriptor.Scoped<IOtpMessageComposer, OtpMailMessageComposer>());
        return services;
    }
}
