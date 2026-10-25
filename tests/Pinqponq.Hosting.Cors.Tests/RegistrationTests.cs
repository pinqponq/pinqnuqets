using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Pinqponq.Hosting.Cors.DependencyInjection;
using Xunit;

namespace Pinqponq.Hosting.Cors.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void Section_overload_binds_the_options_and_registers_the_policy()
    {
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedOrigins:0"] = CorsHost.AllowedOrigin })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = "Production" });

        services.AddPinqponqCors(section);

        using var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = corsOptions.GetPolicy(corsOptions.DefaultPolicyName);
        Assert.Equal([CorsHost.AllowedOrigin], provider.GetRequiredService<IOptions<PinqponqCorsOptions>>().Value.AllowedOrigins);
        Assert.Equal([CorsHost.AllowedOrigin], policy!.Origins);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqCors(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqCors((Action<PinqponqCorsOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqCors(null!, new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqCors((IConfiguration)null!));
    }
}
