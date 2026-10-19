using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pinqponq.Hosting.Periodic.DependencyInjection;
using Xunit;

namespace Pinqponq.Hosting.Periodic.Tests;

public sealed class PeriodicJobHealthCheckTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Job_not_started_yet_is_healthy()
    {
        await using var harness = CreateHarness(options => options.Interval = Interval);

        var entry = await CheckHealth(harness);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
        Assert.Contains("has not started yet", entry.Description);
    }

    [Fact]
    public async Task Health_check_registered_without_the_job_reports_failure_status()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddPinqponqPeriodicJob<ProbeJob>(staleAfter: Interval);
        await using var serviceProvider = services.BuildServiceProvider();

        var report = await serviceProvider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = report.Entries[nameof(ProbeJob)];
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("ProbeJob is not registered", entry.Description);
    }

    [Fact]
    public async Task Disabled_job_is_healthy()
    {
        await using var harness = CreateHarness(options =>
        {
            options.Interval = Interval;
            options.IsEnabled = false;
        });

        await harness.Start();
        await Eventually.True(() => harness.JobService.ExecuteTask!.IsCompleted, "service finished");
        harness.Time.Advance(Interval * 10);

        var entry = await CheckHealth(harness);
        Assert.Equal(HealthStatus.Healthy, entry.Status);
        Assert.Contains("is disabled", entry.Description);
    }

    [Fact]
    public async Task Job_running_on_schedule_is_healthy_with_last_success_time()
    {
        await using var harness = CreateHarness(options => options.Interval = Interval);

        await harness.Start();
        var firstRunAt = harness.Time.GetUtcNow();
        await harness.WaitForLastSuccessAt(firstRunAt);
        harness.Time.Advance(Interval);
        await harness.WaitForLastSuccessAt(firstRunAt + Interval);
        harness.Time.Advance(Interval * 2 - TimeSpan.FromSeconds(1));

        var entry = await CheckHealth(harness);
        Assert.Equal(HealthStatus.Healthy, entry.Status);
        Assert.Contains("on schedule", entry.Description);
    }

    [Fact]
    public async Task Job_without_success_for_two_intervals_is_unhealthy_with_last_failure_time()
    {
        await using var harness = CreateHarness(options => options.Interval = Interval);
        harness.Probe.Behavior = (runNumber, _) => runNumber == 1 ? Task.CompletedTask : Task.FromException(new InvalidOperationException("boom"));

        await harness.Start();
        var lastSuccessAt = harness.Time.GetUtcNow();
        await harness.WaitForLastSuccessAt(lastSuccessAt);
        await harness.AdvanceAndWaitForRunCount(Interval, 2);
        await harness.AdvanceAndWaitForRunCount(Interval, 3);
        await Eventually.True(() => harness.ErrorLogs.Count == 2, "two failures logged");

        var atLimit = await CheckHealth(harness);
        Assert.Equal(HealthStatus.Healthy, atLimit.Status);

        harness.Time.Advance(TimeSpan.FromSeconds(1));
        var entry = await CheckHealth(harness);
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Equal(lastSuccessAt, entry.Data["startedAt"]);
        Assert.Equal(lastSuccessAt, entry.Data["lastSuccessAt"]);
        Assert.Equal(lastSuccessAt + (Interval * 2), entry.Data["lastFailureAt"]);
        Assert.Contains("for longer than", entry.Description);
    }

    [Fact]
    public async Task Job_that_never_succeeded_is_unhealthy_only_after_startup_delay_and_stale_time()
    {
        var startupDelay = TimeSpan.FromMinutes(1);
        var staleAfter = TimeSpan.FromMinutes(7);
        await using var harness = CreateHarness(
            options =>
            {
                options.Interval = Interval;
                options.StartupDelay = startupDelay;
            },
            staleAfter);
        harness.Probe.Behavior = (_, _) => Task.FromException(new InvalidOperationException("boom"));

        await harness.StartAndWaitForFirstTimer();
        harness.Time.Advance(startupDelay + staleAfter);
        var atLimit = await CheckHealth(harness);
        Assert.Equal(HealthStatus.Healthy, atLimit.Status);

        harness.Time.Advance(TimeSpan.FromSeconds(1));
        var entry = await CheckHealth(harness);
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("since it started", entry.Description);
        Assert.False(entry.Data.ContainsKey("lastSuccessAt"));
    }

    [Fact]
    public async Task Health_check_uses_given_name_failure_status_and_tags()
    {
        await using var harness = PeriodicJobHarness.Create(
            options => options.Interval = Interval,
            services => services.AddHealthChecks().AddPinqponqPeriodicJob<ProbeJob>(
                name: "probe",
                staleAfter: TimeSpan.FromMinutes(1),
                failureStatus: HealthStatus.Degraded,
                tags: ["jobs"]));
        harness.Probe.Behavior = (_, _) => Task.FromException(new InvalidOperationException("boom"));

        await harness.StartAndWaitForFirstTimer();
        await Eventually.True(() => harness.ErrorLogs.Count == 1, "first failure");
        harness.Time.Advance(TimeSpan.FromMinutes(2));
        var report = await harness.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = report.Entries["probe"];
        Assert.Equal(HealthStatus.Degraded, entry.Status);
        Assert.Equal(["jobs"], entry.Tags);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_stale_time_throws(int staleSeconds)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddHealthChecks().AddPinqponqPeriodicJob<ProbeJob>(staleAfter: TimeSpan.FromSeconds(staleSeconds)));

        Assert.Equal("staleAfter", exception.ParamName);
        Assert.Contains("must be greater than zero", exception.Message);
    }

    private static PeriodicJobHarness CreateHarness(Action<PeriodicJobOptions> configure, TimeSpan? staleAfter = null) =>
        PeriodicJobHarness.Create(
            configure,
            services => services.AddHealthChecks().AddPinqponqPeriodicJob<ProbeJob>(staleAfter: staleAfter));

    private static async Task<HealthReportEntry> CheckHealth(PeriodicJobHarness harness)
    {
        var report = await harness.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        return report.Entries[nameof(ProbeJob)];
    }
}
