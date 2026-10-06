using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Identity.Otp.DependencyInjection;
using Xunit;

namespace Pinqponq.Identity.Otp.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqOtp_registers_service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOtpStore, InMemoryOtpStore>();
        services.AddSingleton<Pinqponq.Sms.ISmsSender, CapturingSmsSender>();
        services.AddSingleton<Pinqponq.Mail.IEmailSender, CapturingEmailSender>();
        services.AddPinqponqOtp(o =>
        {
            o.Ttl = TimeSpan.FromMinutes(2);
            o.HashPepper = "0123456789abcdef0123456789abcdef";
        });

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IOtpService>());
        Assert.IsType<AllowAllOtpSendRateLimiter>(sp.GetService<IOtpSendRateLimiter>());
    }

    /// <summary>
    /// ASP.NET Core builds its container with these options in Development. The store and
    /// the senders belong to the consuming application, and may be registered after this
    /// call or — for a channel the application does not use — not at all.
    /// </summary>
    [Fact]
    public void AddPinqponqOtp_passes_container_validation_with_nothing_else_registered()
    {
        var services = new ServiceCollection();
        services.AddPinqponqOtp();

        var build = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.Null(Record.Exception(build));
    }

    [Fact]
    public async Task An_email_only_application_needs_no_sms_sender()
    {
        var mail = new CapturingEmailSender();
        var services = new ServiceCollection();
        services.AddSingleton<IOtpStore, InMemoryOtpStore>();
        services.AddSingleton<Pinqponq.Mail.IEmailSender>(mail);
        services.AddPinqponqOtp(o => o.HashPepper = "0123456789abcdef0123456789abcdef");

        using var provider = services.BuildServiceProvider();
        var otp = provider.GetRequiredService<IOtpService>();

        await otp.GenerateAndSendAsync("user@example.com");

        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task Routing_to_a_channel_with_no_sender_names_the_registration_to_add()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOtpStore, InMemoryOtpStore>();
        services.AddSingleton<Pinqponq.Mail.IEmailSender, CapturingEmailSender>();
        services.AddPinqponqOtp(o => o.HashPepper = "0123456789abcdef0123456789abcdef");

        using var provider = services.BuildServiceProvider();
        var otp = provider.GetRequiredService<IOtpService>();

        var send = () => otp.GenerateAndSendAsync("+905550000000", OtpChannel.Sms);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(send);
        Assert.Contains("AddPinqponqSms", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolving_the_service_without_a_store_names_what_is_missing()
    {
        var services = new ServiceCollection();
        services.AddPinqponqOtp();

        using var provider = services.BuildServiceProvider();

        var resolve = provider.GetRequiredService<IOtpService>;

        var exception = Assert.ThrowsAny<InvalidOperationException>(resolve);
        Assert.Contains(nameof(IOtpStore), exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
