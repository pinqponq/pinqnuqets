using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pinqponq.Hosting.Periodic;

internal sealed class PeriodicJobHealthCheck : IHealthCheck
{
    private readonly string _jobKey;
    private readonly string _jobName;
    private readonly PeriodicJobOptions _jobOptions;
    private readonly TimeSpan _staleAfter;
    private readonly PeriodicJobRunRegistry _runRegistry;
    private readonly TimeProvider _timeProvider;

    public PeriodicJobHealthCheck(
        string jobKey,
        string jobName,
        PeriodicJobOptions jobOptions,
        TimeSpan staleAfter,
        PeriodicJobRunRegistry runRegistry,
        TimeProvider timeProvider)
    {
        _jobKey = jobKey;
        _jobName = jobName;
        _jobOptions = jobOptions;
        _staleAfter = staleAfter;
        _runRegistry = runRegistry;
        _timeProvider = timeProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var runStatus = _runRegistry.Find(_jobKey);
        var result = runStatus is null ? CheckNotStarted() : CheckRunStatus(runStatus, context.Registration.FailureStatus);
        return Task.FromResult(result);
    }

    private HealthCheckResult CheckNotStarted()
    {
        var description = $"Periodic job {_jobName} has not started yet.";
        return HealthCheckResult.Healthy(description);
    }

    private HealthCheckResult CheckRunStatus(PeriodicJobRunStatus runStatus, HealthStatus failureStatus)
    {
        if (!runStatus.IsEnabled)
        {
            return HealthCheckResult.Healthy($"Periodic job {_jobName} is disabled.");
        }

        var data = CreateData(runStatus);
        var deadline = CalculateDeadline(runStatus);
        if (_timeProvider.GetUtcNow() <= deadline)
        {
            return HealthCheckResult.Healthy($"Periodic job {_jobName} is running on schedule.", data);
        }

        var description = runStatus.LastSuccessAt is null
            ? $"Periodic job {_jobName} has not finished a run successfully since it started."
            : $"Periodic job {_jobName} has not finished a run successfully for longer than {_staleAfter}.";
        return new HealthCheckResult(failureStatus, description, data: data);
    }

    private DateTimeOffset CalculateDeadline(PeriodicJobRunStatus runStatus)
    {
        var firstRunAt = runStatus.StartedAt + _jobOptions.StartupDelay;
        var lastExpectedRunAt = runStatus.LastSuccessAt ?? firstRunAt;
        return lastExpectedRunAt + _staleAfter;
    }

    private static Dictionary<string, object> CreateData(PeriodicJobRunStatus runStatus)
    {
        var data = new Dictionary<string, object> { ["startedAt"] = runStatus.StartedAt };
        if (runStatus.LastSuccessAt is { } lastSuccessAt)
        {
            data["lastSuccessAt"] = lastSuccessAt;
        }

        if (runStatus.LastFailureAt is { } lastFailureAt)
        {
            data["lastFailureAt"] = lastFailureAt;
        }

        return data;
    }
}
