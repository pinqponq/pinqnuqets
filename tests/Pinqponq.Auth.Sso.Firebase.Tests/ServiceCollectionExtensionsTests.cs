using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Auth.Sso.Abstractions;
using Pinqponq.Auth.Sso.Firebase.DependencyInjection;
using Xunit;

namespace Pinqponq.Auth.Sso.Firebase.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task Missing_ProjectId_fails_host_startup()
    {
        using var host = BuildHost(_ => { });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(exception.Failures, failure => failure.Contains("ProjectId"));
    }

    [Fact]
    public async Task Valid_options_start_the_host()
    {
        using var host = BuildHost(options => options.ProjectId = FirebaseTestServer.ProjectId);

        await host.StartAsync();
        await host.StopAsync();

        Assert.Equal("Firebase", host.Services.GetRequiredService<FirebaseAuthProvider>().ProviderName);
    }

    [Fact]
    public void Provider_is_not_registered_as_IExternalAuthProvider()
    {
        var services = new ServiceCollection();
        services.AddPinqponqFirebaseSso(options => options.ProjectId = FirebaseTestServer.ProjectId);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Null(serviceProvider.GetService<IExternalAuthProvider>());
        Assert.Same(
            serviceProvider.GetRequiredService<FirebaseAuthProvider>(),
            serviceProvider.GetRequiredService<FirebaseAuthProvider>());
    }

    [Fact]
    public void Options_have_the_documented_defaults()
    {
        var options = new FirebaseAuthOptions();

        Assert.Null(options.ProjectId);
        Assert.Empty(options.AllowedSignInProviders);
        Assert.False(options.RequireEmailVerified);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ClockSkew);
        Assert.Equal(TimeSpan.FromHours(1), options.KeysCacheDurationWithoutMaxAge);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MinimumUnknownKeyRefreshInterval);
        Assert.Equal(
            new Uri("https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com"),
            options.PublicKeysUri);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ServiceCollectionExtensions.AddPinqponqFirebaseSso(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqFirebaseSso(null!));
    }

    private static IHost BuildHost(Action<FirebaseAuthOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddPinqponqFirebaseSso(configure);
        return builder.Build();
    }
}
