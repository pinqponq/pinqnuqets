using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pinqponq.Mail.DependencyInjection;

/// <summary>
/// Registration helpers for SMTP mail sending.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IEmailSender"/> with an inline configuration action.</summary>
    public static IServiceCollection AddPinqponqMail(
        this IServiceCollection services,
        Action<SmtpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }

    /// <summary>Registers <see cref="IEmailSender"/> bound to a configuration section.</summary>
    public static IServiceCollection AddPinqponqMail(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Smtp")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(sectionName);

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException(
                $"Configuration section '{sectionName}' not found. Add SMTP configuration to appsettings.");
        }

        services.Configure<SmtpOptions>(section);
        services.TryAddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
