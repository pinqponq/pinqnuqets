using FluentAssertions;
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
        services.AddPinqponqOtp(o => o.Ttl = TimeSpan.FromMinutes(2));

        using var sp = services.BuildServiceProvider();
        sp.GetService<IOtpService>().Should().NotBeNull();
    }
}
