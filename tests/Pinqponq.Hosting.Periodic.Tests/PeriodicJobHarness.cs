using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pinqponq.Hosting.Periodic.DependencyInjection;

namespace Pinqponq.Hosting.Periodic.Tests;

internal sealed class PeriodicJobHarness : IAsyncDisposable
{
    private PeriodicJobHarness(ServiceProvider services, TimerCountingTimeProvider time, JobProbe probe, CapturingLoggerProvider logProvider)
    {
        Services = services;
        Time = time;
        Probe = probe;
        LogProvider = logProvider;
    }

    public ServiceProvider Services { get; }

    public TimerCountingTimeProvider Time { get; }

    public JobProbe Probe { get; }

    public CapturingLoggerProvider LogProvider { get; }

    public BackgroundService JobService =>
        Services.GetServices<IHostedService>().OfType<BackgroundService>().SingleOrDefault()
        ?? throw new InvalidOperationException("Expected exactly one periodic job hosted service.");

    public IReadOnlyList<CapturedLog> ErrorLogs =>
        LogProvider.Logs.Where(log => log.Level == LogLevel.Error).ToList();

    public static PeriodicJobHarness Create(
        Action<PeriodicJobOptions> configure,
        Action<IServiceCollection>? configureServices = null)
    {
        var time = new TimerCountingTimeProvider();
        var probe = new JobProbe();
        var logProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logProvider));
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(probe);
        services.AddScoped<ScopedDependency>();
        configureServices?.Invoke(services);
        services.AddPinqponqPeriodicJob<ProbeJob>(configure);
        return new PeriodicJobHarness(services.BuildServiceProvider(), time, probe, logProvider);
    }

    public Task Start() => JobService.StartAsync(CancellationToken.None);

    // From .NET 10 the host runs ExecuteAsync on a thread pool thread, so the job may schedule its
    // timer after StartAsync returns. Advancing the fake clock before that would be lost.
    public async Task StartAndWaitForFirstTimer()
    {
        await Start();
        await Eventually.True(() => Time.ScheduledTimerCount >= 1, "first timer");
    }

    public async Task AdvanceAndWaitForRunCount(TimeSpan duration, int expectedRunCount)
    {
        Time.Advance(duration);
        await Eventually.True(() => Probe.RunCount >= expectedRunCount, $"run count {expectedRunCount}");
    }

    public Task WaitForLastSuccessAt(DateTimeOffset expectedLastSuccessAt)
    {
        var runRegistry = Services.GetRequiredService<PeriodicJobRunRegistry>();
        var jobKey = PeriodicJobKey.For<ProbeJob>();
        return Eventually.True(
            () => runRegistry.Find(jobKey)?.LastSuccessAt == expectedLastSuccessAt,
            $"last success at {expectedLastSuccessAt}");
    }

    public async ValueTask DisposeAsync()
    {
        await JobService.StopAsync(CancellationToken.None);
        await Services.DisposeAsync();
    }
}

internal sealed class TimerCountingTimeProvider : FakeTimeProvider
{
    private int _scheduledTimerCount;

    // Counts timers that have a due time. Task.Delay and PeriodicTimer create their timer first and
    // set the due time with a later Change call, so counting created timers is not enough.
    public int ScheduledTimerCount => Volatile.Read(ref _scheduledTimerCount);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new SchedulingTimer(base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan), this);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class SchedulingTimer(ITimer innerTimer, TimerCountingTimeProvider timeProvider) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            var isChanged = innerTimer.Change(dueTime, period);
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                Interlocked.Increment(ref timeProvider._scheduledTimerCount);
            }

            return isChanged;
        }

        public void Dispose() => innerTimer.Dispose();

        public ValueTask DisposeAsync() => innerTimer.DisposeAsync();
    }
}
