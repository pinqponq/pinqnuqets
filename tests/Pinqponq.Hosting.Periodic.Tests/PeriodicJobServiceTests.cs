using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Pinqponq.Hosting.Periodic.Tests;

public sealed class PeriodicJobServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Disabled_job_never_runs()
    {
        await using var harness = PeriodicJobHarness.Create(options =>
        {
            options.Interval = Interval;
            options.IsEnabled = false;
        });

        await harness.Start();
        await Eventually.True(() => harness.JobService.ExecuteTask!.IsCompleted, "service finished");
        harness.Time.Advance(Interval * 10);
        await Eventually.Settle();

        Assert.Equal(0, harness.Probe.RunCount);
        Assert.True(harness.JobService.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Contains(harness.LogProvider.Logs, log => log.Level == LogLevel.Information && log.Message.Contains("ProbeJob is disabled"));
    }

    [Fact]
    public async Task First_run_starts_right_away_when_run_immediately()
    {
        await using var harness = PeriodicJobHarness.Create(options => options.Interval = Interval);

        await harness.Start();

        await Eventually.True(() => harness.Probe.RunCount == 1, "first run");
        await harness.AdvanceAndWaitForRunCount(Interval, 2);
        await harness.AdvanceAndWaitForRunCount(Interval, 3);
        await Eventually.True(
            () => harness.LogProvider.Logs.Count(log => log.Level == LogLevel.Debug && log.Message.Contains("ProbeJob finished in")) == 3,
            "three finished logs");
    }

    [Fact]
    public async Task First_run_waits_one_interval_when_not_run_immediately()
    {
        await using var harness = PeriodicJobHarness.Create(options =>
        {
            options.Interval = Interval;
            options.RunImmediately = false;
        });

        await harness.StartAndWaitForFirstTimer();
        harness.Time.Advance(Interval - TimeSpan.FromSeconds(1));
        await Eventually.Settle();
        Assert.Equal(0, harness.Probe.RunCount);

        await harness.AdvanceAndWaitForRunCount(TimeSpan.FromSeconds(1), 1);
    }

    [Fact]
    public async Task First_run_waits_for_startup_delay()
    {
        var startupDelay = TimeSpan.FromMinutes(1);
        await using var harness = PeriodicJobHarness.Create(options =>
        {
            options.Interval = Interval;
            options.StartupDelay = startupDelay;
        });

        await harness.StartAndWaitForFirstTimer();
        harness.Time.Advance(startupDelay - TimeSpan.FromSeconds(1));
        await Eventually.Settle();
        Assert.Equal(0, harness.Probe.RunCount);

        await harness.AdvanceAndWaitForRunCount(TimeSpan.FromSeconds(1), 1);
        harness.Time.Advance(Interval - TimeSpan.FromSeconds(1));
        await Eventually.Settle();
        Assert.Equal(1, harness.Probe.RunCount);

        await harness.AdvanceAndWaitForRunCount(TimeSpan.FromSeconds(1), 2);
    }

    [Fact]
    public async Task Failing_run_is_logged_with_job_name_and_next_run_still_starts()
    {
        var failure = new InvalidOperationException("sweep failed");
        await using var harness = PeriodicJobHarness.Create(options => options.Interval = Interval);
        harness.Probe.Behavior = (runNumber, _) => runNumber == 1 ? Task.FromException(failure) : Task.CompletedTask;

        await harness.Start();
        await Eventually.True(() => harness.ErrorLogs.Count == 1, "error log");
        await harness.AdvanceAndWaitForRunCount(Interval, 2);
        await Eventually.Settle();

        var errorLog = Assert.Single(harness.ErrorLogs);
        Assert.Same(failure, errorLog.Exception);
        Assert.Contains("ProbeJob failed", errorLog.Message);
    }

    [Fact]
    public async Task Cancellation_not_caused_by_stopping_is_logged_as_failure_and_next_run_still_starts()
    {
        await using var harness = PeriodicJobHarness.Create(options => options.Interval = Interval);
        harness.Probe.Behavior = (runNumber, _) => runNumber == 1
            ? Task.FromException(new TaskCanceledException("request timed out"))
            : Task.CompletedTask;

        await harness.Start();
        await Eventually.True(() => harness.ErrorLogs.Count == 1, "error log");
        await harness.AdvanceAndWaitForRunCount(Interval, 2);

        Assert.IsType<TaskCanceledException>(Assert.Single(harness.ErrorLogs).Exception);
    }

    [Fact]
    public async Task Stopping_during_run_ends_loop_without_error_log()
    {
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = PeriodicJobHarness.Create(options => options.Interval = Interval);
        harness.Probe.Behavior = async (_, cancellationToken) =>
        {
            runStarted.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        };

        await harness.Start();
        await runStarted.Task;
        await harness.JobService.StopAsync(CancellationToken.None);

        Assert.True(harness.JobService.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(harness.ErrorLogs);
        Assert.Contains(harness.LogProvider.Logs, log => log.Message.Contains("ProbeJob stopped because the host is stopping"));
    }

    [Fact]
    public async Task Stopping_while_waiting_for_next_run_ends_loop_without_error_log()
    {
        await using var harness = PeriodicJobHarness.Create(options => options.Interval = Interval);

        await harness.Start();
        await Eventually.True(() => harness.Probe.RunCount == 1, "first run");
        await harness.JobService.StopAsync(CancellationToken.None);
        harness.Time.Advance(Interval * 3);
        await Eventually.Settle();

        Assert.True(harness.JobService.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, harness.Probe.RunCount);
        Assert.Empty(harness.ErrorLogs);
    }

    [Fact]
    public async Task Each_run_gets_its_own_scope_that_is_disposed_after_the_run()
    {
        await using var harness = PeriodicJobHarness.Create(options => options.Interval = Interval);

        await harness.Start();
        await Eventually.True(() => harness.Probe.RunCount == 1, "first run");
        await harness.AdvanceAndWaitForRunCount(Interval, 2);
        await Eventually.True(() => harness.Probe.SeenDependencies.All(dependency => dependency.IsDisposed), "scopes disposed");

        Assert.Equal(2, harness.Probe.SeenDependencies.Count);
        Assert.NotSame(harness.Probe.SeenDependencies[0], harness.Probe.SeenDependencies[1]);
    }

    [Fact]
    public async Task Configured_name_is_used_in_logs()
    {
        await using var harness = PeriodicJobHarness.Create(options =>
        {
            options.Interval = Interval;
            options.Name = "media-expiry";
        });
        harness.Probe.Behavior = (_, _) => Task.FromException(new InvalidOperationException("boom"));

        await harness.Start();
        await Eventually.True(() => harness.ErrorLogs.Count == 1, "error log");

        Assert.Contains("Periodic job media-expiry failed", Assert.Single(harness.ErrorLogs).Message);
    }

    [Fact]
    public async Task Observer_receives_start_and_success_with_duration()
    {
        var observer = new RecordingObserver();
        var runDuration = TimeSpan.FromSeconds(3);
        await using var harness = PeriodicJobHarness.Create(
            options => options.Interval = Interval,
            services => services.AddSingleton<IPeriodicJobObserver>(observer));
        harness.Probe.Behavior = (_, _) =>
        {
            harness.Time.Advance(runDuration);
            return Task.CompletedTask;
        };

        await harness.Start();
        await Eventually.True(() => observer.Calls.Count == 2, "observer calls");

        Assert.Equal(["started ProbeJob", $"succeeded ProbeJob {runDuration}"], observer.Calls);
    }

    [Fact]
    public async Task Observer_receives_failure_with_exception()
    {
        var observer = new RecordingObserver();
        var failure = new InvalidOperationException("boom");
        await using var harness = PeriodicJobHarness.Create(
            options => options.Interval = Interval,
            services => services.AddSingleton<IPeriodicJobObserver>(observer));
        harness.Probe.Behavior = (_, _) => Task.FromException(failure);

        await harness.Start();
        await Eventually.True(() => observer.Calls.Count == 2, "observer calls");

        Assert.Equal(["started ProbeJob", $"failed ProbeJob {TimeSpan.Zero}"], observer.Calls);
        Assert.Same(failure, observer.LastFailure);
    }

    [Fact]
    public async Task Throwing_observer_is_logged_and_does_not_turn_the_run_into_a_failure()
    {
        var recordingObserver = new RecordingObserver();
        await using var harness = PeriodicJobHarness.Create(
            options => options.Interval = Interval,
            services =>
            {
                services.AddSingleton<IPeriodicJobObserver, ThrowingObserver>();
                services.AddSingleton<IPeriodicJobObserver>(recordingObserver);
            });

        await harness.Start();
        await Eventually.True(() => recordingObserver.Calls.Count == 2, "observer calls");

        Assert.Equal(["started ProbeJob", $"succeeded ProbeJob {TimeSpan.Zero}"], recordingObserver.Calls);
        Assert.Empty(harness.ErrorLogs);
        Assert.Equal(2, harness.LogProvider.Logs.Count(log => log.Level == LogLevel.Warning && log.Message.Contains("ThrowingObserver")));
    }

    [Fact]
    public async Task Gate_that_refuses_skips_the_run()
    {
        var gate = new RecordingGate(allowsEntry: false);
        await using var harness = PeriodicJobHarness.Create(
            options => options.Interval = Interval,
            services => services.AddSingleton<IPeriodicJobGate>(gate));

        await harness.Start();
        await Eventually.True(() => gate.EnteredJobNames.Count == 1, "gate asked");
        await Eventually.Settle();

        Assert.Equal(0, harness.Probe.RunCount);
        Assert.Equal(["ProbeJob"], gate.EnteredJobNames);
        Assert.Empty(harness.ErrorLogs);
        Assert.Contains(harness.LogProvider.Logs, log => log.Level == LogLevel.Debug && log.Message.Contains("ProbeJob skipped this run because another run holds the gate"));
    }

    [Fact]
    public async Task Gate_that_allows_runs_the_job_and_releases_the_lease_afterwards()
    {
        var gate = new RecordingGate(allowsEntry: true);
        await using var harness = PeriodicJobHarness.Create(
            options => options.Interval = Interval,
            services => services.AddSingleton<IPeriodicJobGate>(gate));
        harness.Probe.Behavior = (_, _) =>
        {
            Assert.Equal(0, gate.ReleasedLeaseCount);
            return Task.CompletedTask;
        };

        await harness.Start();
        await Eventually.True(() => gate.ReleasedLeaseCount == 1, "lease released");

        Assert.Equal(1, harness.Probe.RunCount);
        Assert.Empty(harness.ErrorLogs);
    }

    [Fact]
    public async Task Gate_that_throws_is_logged_as_failure_and_next_run_still_asks_again()
    {
        var gate = new ThrowingGate();
        await using var harness = PeriodicJobHarness.Create(
            options => options.Interval = Interval,
            services => services.AddSingleton<IPeriodicJobGate>(gate));

        await harness.Start();
        await Eventually.True(() => harness.ErrorLogs.Count == 1, "error log");
        harness.Time.Advance(Interval);
        await Eventually.True(() => harness.ErrorLogs.Count == 2, "second error log");

        Assert.Equal(0, harness.Probe.RunCount);
    }

    private sealed class RecordingObserver : IPeriodicJobObserver
    {
        public List<string> Calls { get; } = [];

        public Exception? LastFailure { get; private set; }

        public void OnStarted(string jobName) => Calls.Add($"started {jobName}");

        public void OnSucceeded(string jobName, TimeSpan duration) => Calls.Add($"succeeded {jobName} {duration}");

        public void OnFailed(string jobName, Exception exception, TimeSpan duration)
        {
            LastFailure = exception;
            Calls.Add($"failed {jobName} {duration}");
        }
    }

    private sealed class ThrowingObserver : IPeriodicJobObserver
    {
        public void OnStarted(string jobName) => throw new InvalidOperationException("observer started failed");

        public void OnSucceeded(string jobName, TimeSpan duration) => throw new InvalidOperationException("observer succeeded failed");

        public void OnFailed(string jobName, Exception exception, TimeSpan duration) => throw new InvalidOperationException("observer failed failed");
    }

    private sealed class RecordingGate(bool allowsEntry) : IPeriodicJobGate
    {
        private int _releasedLeaseCount;

        public List<string> EnteredJobNames { get; } = [];

        public int ReleasedLeaseCount => Volatile.Read(ref _releasedLeaseCount);

        public Task<IAsyncDisposable?> TryEnter(string jobName, CancellationToken cancellationToken)
        {
            EnteredJobNames.Add(jobName);
            IAsyncDisposable? lease = allowsEntry ? new Lease(this) : null;
            return Task.FromResult(lease);
        }

        private sealed class Lease(RecordingGate gate) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref gate._releasedLeaseCount);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ThrowingGate : IPeriodicJobGate
    {
        public Task<IAsyncDisposable?> TryEnter(string jobName, CancellationToken cancellationToken) =>
            Task.FromException<IAsyncDisposable?>(new InvalidOperationException("lock store unreachable"));
    }
}
