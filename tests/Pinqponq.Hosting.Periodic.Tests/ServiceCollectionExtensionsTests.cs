using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Hosting.Periodic.DependencyInjection;
using Xunit;

namespace Pinqponq.Hosting.Periodic.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task Invalid_options_fail_host_startup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<JobProbe>();
        builder.Services.AddScoped<ScopedDependency>();
        builder.Services.AddPinqponqPeriodicJob<ProbeJob>(options => options.StartupDelay = TimeSpan.FromSeconds(-1));
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(exception.Failures, failure => failure.Contains("Interval"));
        Assert.Contains(exception.Failures, failure => failure.Contains("StartupDelay"));
        Assert.Equal(0, host.Services.GetRequiredService<JobProbe>().RunCount);
    }

    [Fact]
    public async Task Valid_options_start_the_host_and_run_the_job()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<JobProbe>();
        builder.Services.AddScoped<ScopedDependency>();
        builder.Services.AddPinqponqPeriodicJob<ProbeJob>(options => options.Interval = TimeSpan.FromHours(1));
        using var host = builder.Build();

        await host.StartAsync();
        var probe = host.Services.GetRequiredService<JobProbe>();
        await Eventually.True(() => probe.RunCount == 1, "first run");
        await host.StopAsync();
    }

    [Fact]
    public void Configuration_section_is_bound_to_the_job_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:Probe:Name"] = "probe-job",
                ["Jobs:Probe:Interval"] = "00:05:00",
                ["Jobs:Probe:StartupDelay"] = "00:01:00",
                ["Jobs:Probe:IsEnabled"] = "false",
                ["Jobs:Probe:RunImmediately"] = "false",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddPinqponqPeriodicJob<ProbeJob>(configuration.GetSection("Jobs:Probe"));
        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<PeriodicJobOptions>>().Get(PeriodicJobKey.For<ProbeJob>());

        Assert.Equal("probe-job", options.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Interval);
        Assert.Equal(TimeSpan.FromMinutes(1), options.StartupDelay);
        Assert.False(options.IsEnabled);
        Assert.False(options.RunImmediately);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ProbeJob));
    }

    [Fact]
    public void Registration_adds_one_hosted_service_and_a_scoped_job()
    {
        var services = new ServiceCollection();

        services.AddPinqponqPeriodicJob<ProbeJob>(options => options.Interval = TimeSpan.FromMinutes(1));
        services.AddPinqponqPeriodicJob<ProbeJob>(options => options.Interval = TimeSpan.FromMinutes(2));

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        var jobDescriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ProbeJob));
        Assert.Equal(ServiceLifetime.Scoped, jobDescriptor.Lifetime);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IValidateOptions<PeriodicJobOptions>));
    }

    [Fact]
    public void Registration_keeps_a_time_provider_the_application_registered()
    {
        var applicationTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(applicationTime);

        services.AddPinqponqPeriodicJob<ProbeJob>(options => options.Interval = TimeSpan.FromMinutes(1));
        using var serviceProvider = services.BuildServiceProvider();

        Assert.Same(applicationTime, serviceProvider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Null_arguments_throw()
    {
        var services = new ServiceCollection();

        var configureException = Assert.Throws<ArgumentNullException>(() => services.AddPinqponqPeriodicJob<ProbeJob>((Action<PeriodicJobOptions>)null!));
        var sectionException = Assert.Throws<ArgumentNullException>(() => services.AddPinqponqPeriodicJob<ProbeJob>((IConfiguration)null!));
        Assert.Equal("configure", configureException.ParamName);
        Assert.Equal("configurationSection", sectionException.ParamName);
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqPeriodicJob<ProbeJob>(_ => { }));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqPeriodicJob<ProbeJob>(new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => ((IHealthChecksBuilder)null!).AddPinqponqPeriodicJob<ProbeJob>());
    }
}
