using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Observability;
using Pinqponq.Push.Fcm.DependencyInjection;
using Xunit;

namespace Pinqponq.Push.Fcm.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogService, RecordingLogService>();
        return services;
    }

    [Fact]
    public void Registers_the_sender_and_the_default_invalid_token_handler()
    {
        var services = NewServices();
        services.AddPinqponqFcm(options => options.IsEnabled = false);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<FcmSender>(scope.ServiceProvider.GetRequiredService<IFcmSender>());
        Assert.IsType<NullFcmInvalidTokenHandler>(scope.ServiceProvider.GetRequiredService<IFcmInvalidTokenHandler>());
        Assert.Same(provider.GetRequiredService<FirebaseAppProvider>(), provider.GetRequiredService<FirebaseAppProvider>());
    }

    [Fact]
    public async Task Default_invalid_token_handler_does_nothing()
    {
        await new NullFcmInvalidTokenHandler().HandleInvalidTokens(["token-a"]);
    }

    [Fact]
    public void Handler_registered_by_the_application_replaces_the_default()
    {
        var services = NewServices();
        services.AddPinqponqFcm(options => options.IsEnabled = false);
        services.AddScoped<IFcmInvalidTokenHandler, RecordingInvalidTokenHandler>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<RecordingInvalidTokenHandler>(scope.ServiceProvider.GetRequiredService<IFcmInvalidTokenHandler>());
    }

    [Fact]
    public void Binds_the_options_from_a_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Fcm:IsEnabled"] = "true",
                ["Fcm:CredentialJson"] = TestServiceAccount.CreateJson(),
                ["Fcm:DataOnlyAndroidPriority"] = "High",
                ["Fcm:IsSendFailureLoggingEnabled"] = "false",
            })
            .Build();
        var services = NewServices();
        services.AddPinqponqFcm(configuration.GetSection(FcmOptions.SectionName));
        using var provider = services.BuildServiceProvider();

        var fcmOptions = provider.GetRequiredService<IOptions<FcmOptions>>().Value;

        Assert.Equal(FirebaseAdmin.Messaging.Priority.High, fcmOptions.DataOnlyAndroidPriority);
        Assert.False(fcmOptions.IsSendFailureLoggingEnabled);
        Assert.NotNull(fcmOptions.CredentialJson);
    }

    [Fact]
    public void Missing_configuration_section_throws_on_registration()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => NewServices().AddPinqponqFcm(configuration.GetSection(FcmOptions.SectionName)));

        Assert.Contains("'Fcm'", exception.Message);
    }

    [Fact]
    public void Invalid_options_fail_validation()
    {
        var services = NewServices();
        services.AddPinqponqFcm(_ => { });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<FcmOptions>>().Value);
    }

    [Fact]
    public void Registration_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqFcm(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => NewServices().AddPinqponqFcm((Action<FcmOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqFcm(null!, new ConfigurationBuilder().Build().GetSection("Fcm")));
        Assert.Throws<ArgumentNullException>(() => NewServices().AddPinqponqFcm((IConfigurationSection)null!));
    }
}
