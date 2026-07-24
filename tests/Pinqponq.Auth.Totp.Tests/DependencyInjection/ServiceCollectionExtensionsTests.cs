using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Auth.Totp.DependencyInjection;
using Xunit;

namespace Pinqponq.Auth.Totp.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqTotp_registers_service()
    {
        var services = new ServiceCollection();
        services.AddPinqponqTotp(o => o.Issuer = "Pinqponq");

        using var sp = services.BuildServiceProvider();
        var totp = sp.GetRequiredService<ITotpService>();
        totp.Should().NotBeNull();
        totp.GenerateSecret().Should().NotBeNullOrWhiteSpace();
    }
}
