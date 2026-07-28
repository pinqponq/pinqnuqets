using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pinqponq.Mail;
using Pinqponq.Sms;

namespace Pinqponq.Identity.Otp.DependencyInjection;

/// <summary>
/// Registration helpers for OTP.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IOtpService"/>. The consumer must also register
    /// <see cref="IOtpStore"/> and a sender for each channel it uses
    /// (<c>AddPinqponqSms</c> / <c>AddPinqponqMail</c>).
    /// </summary>
    /// <remarks>
    /// Neither the store nor the senders have to be registered before this call, and an
    /// application that only sends one channel need not register the other. What is
    /// missing is reported when the code is sent, naming the registration to add.
    /// </remarks>
    public static IServiceCollection AddPinqponqOtp(
        this IServiceCollection services,
        Action<OtpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Unconditionally: without a configure action nothing else would register the
        // options services, and the defaults this package ships would be unresolvable.
        var options = services.AddOptions<OtpOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OtpOptions>, OtpOptionsValidator>());

        services.TryAddSingleton<IOtpSendRateLimiter, AllowAllOtpSendRateLimiter>();

        // A factory rather than the implementation type: ASP.NET Core validates every
        // type-registered descriptor when it builds the container in Development, and the
        // store and senders belong to the consuming application, so a type registration
        // would turn a missing one into an aborted startup listing this descriptor.
        services.TryAddScoped<IOtpService>(sp => new OtpService(
            sp.GetService<IOtpStore>() ?? throw new InvalidOperationException(
                $"{nameof(IOtpService)} needs an {nameof(IOtpStore)}, which this package does "
                + "not provide. Register the application's own storage, for example "
                + $"services.AddScoped<{nameof(IOtpStore)}, MyOtpStore>()."),
            sp.GetService<ISmsSender>(),
            sp.GetService<IEmailSender>(),
            sp.GetRequiredService<IOtpSendRateLimiter>(),
            sp.GetRequiredService<IOptions<OtpOptions>>()));

        return services;
    }
}
