# Pinqponq.Hosting.Periodic

Runs periodic background jobs in a .NET host. The application writes only the job's work; the
package runs it on a schedule as a hosted service:

- an on/off switch,
- an optional delay after the host starts,
- the first run right away or one interval later,
- a new DI scope for every run, disposed after the run,
- a failed run is logged with the job name, and the next run still starts on schedule,
- stopping the host ends the job without an error log.

## Install

```bash
dotnet add package Pinqponq.Hosting.Periodic
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- A host that runs hosted services (`WebApplication`, `Host.CreateApplicationBuilder`)

## Quick start

Write the job. It is resolved from the run's scope, so its constructor can take scoped services:

```csharp
public sealed class MomentExpiryJob(IMomentRepository momentRepository) : IPeriodicJob
{
    public async Task Run(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        await momentRepository.ExpireOldMoments(cancellationToken);
    }
}
```

Register it with its schedule:

```csharp
builder.Services.AddPinqponqPeriodicJob<MomentExpiryJob>(options =>
{
    options.Interval = TimeSpan.FromMinutes(5);
    options.StartupDelay = TimeSpan.FromMinutes(1);
    options.IsEnabled = momentSettings.ExpiryWorkerEnabled;
});
```

Or bind the options from a configuration section:

```csharp
builder.Services.AddPinqponqPeriodicJob<MomentExpiryJob>(
    builder.Configuration.GetSection("Jobs:MomentExpiry"));
```

```json
{
  "Jobs": {
    "MomentExpiry": {
      "Interval": "00:05:00",
      "StartupDelay": "00:01:00",
      "IsEnabled": true
    }
  }
}
```

Each job type is registered once. Registering the same type again adds no second hosted service.

## Configuration

`PeriodicJobOptions` are validated when the host starts. The host does not start when they are
invalid.

| Option | Default | Notes |
|---|---|---|
| `Interval` | none | The time between the starts of two runs. Required: it must be greater than zero and at most about 49.7 days (the longest period `PeriodicTimer` accepts). |
| `StartupDelay` | zero | The time the job waits after the host starts before its first run. It must not be negative. |
| `IsEnabled` | `true` | When `false`, the job never runs. |
| `RunImmediately` | `true` | When `true`, the first run starts right after `StartupDelay`. When `false`, the first run starts one `Interval` later. |
| `Name` | the job's type name | The name used in log messages, observer calls and health check data. It must not be blank. |

The package has no default interval: each application chooses its own schedule.

## Behavior

| Situation | Result |
|---|---|
| `IsEnabled` is `false` | The job never runs. One information log says it is disabled. |
| A run throws an exception | The exception is logged as an error with the job name and the run's duration. The next run starts on schedule. |
| A run throws `OperationCanceledException` while the host keeps running (for example an HTTP timeout) | Logged as a failed run, like any other exception. |
| The host stops during a run or between runs | The cancellation token passed to `Run` is cancelled and the job ends. No error is logged. |
| A run takes longer than `Interval` | The next run starts right after it. Runs of one job never overlap inside one process. |

Times come from the `TimeProvider` registered in DI (`TimeProvider.System` when the application
registered none), so tests can control them with a fake time provider.

## Observing runs

Register `IPeriodicJobObserver` implementations as singletons to forward run results to your own
log service or metrics. None is registered by default.

```csharp
public sealed class JobMetricsObserver(IJobMetrics jobMetrics) : IPeriodicJobObserver
{
    public void OnStarted(string jobName) => jobMetrics.RunStarted(jobName);

    public void OnSucceeded(string jobName, TimeSpan duration) => jobMetrics.RunSucceeded(jobName, duration);

    public void OnFailed(string jobName, Exception exception, TimeSpan duration) => jobMetrics.RunFailed(jobName, exception, duration);
}

builder.Services.AddSingleton<IPeriodicJobObserver, JobMetricsObserver>();
```

An exception thrown by an observer is logged as a warning. It does not change the run's result
and does not stop the other observers.

## Running a job on one replica at a time

When several replicas of a service run the same job, register one `IPeriodicJobGate` as a
singleton. The package asks it before every run of every periodic job:

- it returns a lease: the run starts, and the lease is disposed when the run ends,
- it returns `null`: this run is skipped, and the job tries again at its next run,
- it throws: the run is logged as failed.

Without a gate, every run starts. The package itself does not reference any lock store; an
implementation can wrap a distributed lock such as `IDistributedLock` from `Pinqponq.Cache`.

## Health check

```csharp
builder.Services.AddHealthChecks()
    .AddPinqponqPeriodicJob<MomentExpiryJob>();
```

The check is unhealthy when the job has not finished a run successfully for longer than
`staleAfter` (default: two intervals). Before the first successful run, the time is counted from
the end of `StartupDelay`. A disabled job, and a job whose hosted service has not started yet, are
healthy. The result data carries `startedAt`, `lastSuccessAt` and `lastFailureAt` when they are
known.

| Parameter | Default |
|---|---|
| `name` | the job's type name |
| `staleAfter` | two intervals; it must be greater than zero |
| `failureStatus` | `Unhealthy` |
| `tags` | none |
