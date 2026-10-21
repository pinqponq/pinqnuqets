namespace Pinqponq.Hosting.Periodic.Tests;

internal sealed class JobProbe
{
    private int _runCount;

    public int RunCount => Volatile.Read(ref _runCount);

    public List<ScopedDependency> SeenDependencies { get; } = [];

    public Func<int, CancellationToken, Task> Behavior { get; set; } = (_, _) => Task.CompletedTask;

    public Task RecordRun(ScopedDependency dependency, CancellationToken cancellationToken)
    {
        lock (SeenDependencies)
        {
            SeenDependencies.Add(dependency);
        }

        var runNumber = Interlocked.Increment(ref _runCount);
        return Behavior(runNumber, cancellationToken);
    }
}

internal sealed class ScopedDependency : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

internal sealed class ProbeJob(JobProbe probe, ScopedDependency dependency) : IPeriodicJob
{
    public Task Run(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        probe.RecordRun(dependency, cancellationToken);
}

internal static class Eventually
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    public static async Task True(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {description}");
            }

            await Task.Delay(PollInterval);
        }
    }

    public static Task Settle() => Task.Delay(TimeSpan.FromMilliseconds(100));
}
