using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.Hosting.Periodic;

internal sealed class PeriodicJobService<TJob> : BackgroundService
    where TJob : class, IPeriodicJob
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly PeriodicJobOptions _jobOptions;
    private readonly TimeProvider _timeProvider;
    private readonly PeriodicJobRunRegistry _runRegistry;
    private readonly IReadOnlyList<IPeriodicJobObserver> _observers;
    private readonly IPeriodicJobGate? _gate;
    private readonly ILogger<PeriodicJobService<TJob>> _logger;
    private readonly string _jobKey = PeriodicJobKey.For<TJob>();
    private readonly string _jobName;

    public PeriodicJobService(
        IServiceScopeFactory serviceScopeFactory,
        IOptionsMonitor<PeriodicJobOptions> jobOptionsMonitor,
        TimeProvider timeProvider,
        PeriodicJobRunRegistry runRegistry,
        IEnumerable<IPeriodicJobObserver> observers,
        IEnumerable<IPeriodicJobGate> gates,
        ILogger<PeriodicJobService<TJob>> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _jobOptions = jobOptionsMonitor.Get(_jobKey);
        _timeProvider = timeProvider;
        _runRegistry = runRegistry;
        _observers = observers.ToList();
        _gate = gates.LastOrDefault();
        _logger = logger;
        _jobName = PeriodicJobKey.ResolveName<TJob>(_jobOptions);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runRegistry.RecordStarted(_jobKey, _jobOptions.IsEnabled, _timeProvider.GetUtcNow());
        if (!_jobOptions.IsEnabled)
        {
            _logger.LogInformation("Periodic job {JobName} is disabled and will not run.", _jobName);
            return;
        }

        try
        {
            await RunOnSchedule(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Periodic job {JobName} stopped because the host is stopping.", _jobName);
        }
    }

    private async Task RunOnSchedule(CancellationToken stoppingToken)
    {
        await Task.Delay(_jobOptions.StartupDelay, _timeProvider, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_jobOptions.Interval, _timeProvider);
        if (_jobOptions.RunImmediately)
        {
            await RunOnce(stoppingToken).ConfigureAwait(false);
        }

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunOnce(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunOnce(CancellationToken stoppingToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        try
        {
            await RunInsideGate(startTimestamp, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsHostStopping(exception, stoppingToken))
        {
            var duration = _timeProvider.GetElapsedTime(startTimestamp);
            _runRegistry.RecordFailure(_jobKey, _timeProvider.GetUtcNow());
            _logger.LogError(exception, "Periodic job {JobName} failed after {DurationMilliseconds} ms.", _jobName, duration.TotalMilliseconds);
            NotifyObservers(observer => observer.OnFailed(_jobName, exception, duration));
        }
    }

    private async Task RunInsideGate(long startTimestamp, CancellationToken stoppingToken)
    {
        if (_gate is null)
        {
            await RunJob(startTimestamp, stoppingToken).ConfigureAwait(false);
            return;
        }

        var gateLease = await _gate.TryEnter(_jobName, stoppingToken).ConfigureAwait(false);
        if (gateLease is null)
        {
            _logger.LogDebug("Periodic job {JobName} skipped this run because another run holds the gate.", _jobName);
            return;
        }

        await using (gateLease.ConfigureAwait(false))
        {
            await RunJob(startTimestamp, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunJob(long startTimestamp, CancellationToken stoppingToken)
    {
        NotifyObservers(observer => observer.OnStarted(_jobName));

        var serviceScope = _serviceScopeFactory.CreateAsyncScope();
        await using (serviceScope.ConfigureAwait(false))
        {
            var job = serviceScope.ServiceProvider.GetRequiredService<TJob>();
            await job.Run(serviceScope.ServiceProvider, stoppingToken).ConfigureAwait(false);
        }

        var duration = _timeProvider.GetElapsedTime(startTimestamp);
        _runRegistry.RecordSuccess(_jobKey, _timeProvider.GetUtcNow());
        _logger.LogDebug("Periodic job {JobName} finished in {DurationMilliseconds} ms.", _jobName, duration.TotalMilliseconds);
        NotifyObservers(observer => observer.OnSucceeded(_jobName, duration));
    }

    private void NotifyObservers(Action<IPeriodicJobObserver> notify)
    {
        foreach (var observer in _observers)
        {
            try
            {
                notify(observer);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Observer {ObserverType} of periodic job {JobName} failed.", observer.GetType().Name, _jobName);
            }
        }
    }

    private static bool IsHostStopping(Exception exception, CancellationToken stoppingToken) =>
        exception is OperationCanceledException && stoppingToken.IsCancellationRequested;
}
