using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.Otp.DependencyInjection;
using Pinqponq.Identity.Otp.Mail.DependencyInjection;
using Pinqponq.Mail;
using Xunit;

namespace Pinqponq.Identity.Otp.Mail.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    private const string Pepper = "0123456789abcdef0123456789abcdef";

    private readonly RecordingEmailSender _emailSender = new();

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOtpStore, InMemoryOtpStore>();
        services.AddSingleton<IEmailSender>(_emailSender);
        return services;
    }

    [Fact]
    public async Task Otp_service_sends_the_branded_mail_when_registered_after_AddPinqponqOtp()
    {
        var services = CreateServices();
        services.AddPinqponqOtp(options => options.HashPepper = Pepper);
        services.AddPinqponqOtpMail(options => options.ProductName = "Example");

        await SendCode(services);

        AssertBrandedMail();
    }

    [Fact]
    public async Task Otp_service_sends_the_branded_mail_when_registered_before_AddPinqponqOtp()
    {
        var services = CreateServices();
        services.AddPinqponqOtpMail(options => options.ProductName = "Example");
        services.AddPinqponqOtp(options => options.HashPepper = Pepper);

        await SendCode(services);

        AssertBrandedMail();
    }

    [Fact]
    public async Task Product_resolver_and_template_replace_the_defaults()
    {
        var template = new RecordingTemplate();
        var services = CreateServices();
        services.AddSingleton<IOtpMailTemplate>(template);
        services.AddPinqponqOtp(options => options.HashPepper = Pepper);
        services.AddPinqponqOtpMail(options =>
        {
            options.ProductName = "Example";
            options.Texts["de"] = OtpMailTexts.CreateEnglishDefaults();
        });
        services.AddScoped<IOtpMailLanguageResolver>(_ => new FixedLanguageResolver("de"));

        await SendCode(services);

        var model = Assert.Single(template.Models);
        Assert.Equal("de", model.Language);
        Assert.Equal("<custom>" + model.Code + "</custom>", Assert.Single(_emailSender.Messages).Body);
    }

    [Fact]
    public void Defaults_are_registered()
    {
        var services = CreateServices();
        services.AddPinqponqOtp(options => options.HashPepper = Pepper);
        services.AddPinqponqOtpMail(options => options.ProductName = "Example");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<OtpMailMessageComposer>(scope.ServiceProvider.GetRequiredService<IOtpMessageComposer>());
        Assert.IsType<OtpMailTemplate>(scope.ServiceProvider.GetRequiredService<IOtpMailTemplate>());
        Assert.IsType<DefaultOtpMailLanguageResolver>(scope.ServiceProvider.GetRequiredService<IOtpMailLanguageResolver>());
    }

    [Fact]
    public void Container_validation_passes()
    {
        var services = CreateServices();
        services.AddPinqponqOtp(options => options.HashPepper = Pepper);
        services.AddPinqponqOtpMail(options => options.ProductName = "Example");

        var build = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.Null(Record.Exception(build));
    }

    [Fact]
    public void Unusable_default_language_fails_options_validation()
    {
        var services = CreateServices();
        services.AddPinqponqOtpMail(options =>
        {
            options.ProductName = "Example";
            options.DefaultLanguage = "de";
        });

        using var provider = services.BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<OtpMailOptions>>().Value;

        var exception = Assert.Throws<OptionsValidationException>(read);
        Assert.Contains("DefaultLanguage 'de' has no Texts.", exception.Failures);
    }

    [Fact]
    public async Task Invalid_options_stop_the_host_on_startup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPinqponqOtpMail(options => options.ProductName = "");
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("ProductName is required.", exception.Failures);
    }

    [Fact]
    public void Texts_and_colors_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OtpMail:ProductName"] = "Example",
                ["OtpMail:DefaultLanguage"] = "de",
                ["OtpMail:Colors:Accent"] = "#123456",
                ["OtpMail:Texts:de:Subject"] = "Ihr Code",
                ["OtpMail:Texts:de:Title"] = "Code",
                ["OtpMail:Texts:de:Body"] = "Text",
                ["OtpMail:Texts:de:LifetimeLabel"] = "{Minutes} Minuten",
            })
            .Build();
        var services = CreateServices();
        services.AddPinqponqOtpMail(options => configuration.GetSection("OtpMail").Bind(options));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OtpMailOptions>>().Value;

        Assert.Equal("Ihr Code", options.Texts["de"].Subject);
        Assert.Equal("Your {ProductName} verification code", options.Texts["en"].Subject);
        Assert.Equal("#123456", options.Colors.Accent);
        Assert.Equal("#ffffff", options.Colors.OnAccent);
    }

    [Fact]
    public void Arguments_are_required()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqOtpMail(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqOtpMail(null!));
    }

    private static async Task SendCode(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IOtpService>().GenerateAndSendAsync("user@example.com");
    }

    private void AssertBrandedMail()
    {
        var message = Assert.Single(_emailSender.Messages);
        Assert.Equal("Your Example verification code", message.Subject);
        Assert.True(message.IsBodyHtml);
        Assert.StartsWith("<!DOCTYPE html>", message.Body);
        Assert.NotNull(message.PlainTextBody);
    }
}
