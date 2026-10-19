namespace Pinqponq.Hosting.Periodic;

/// <summary>
/// Decides whether a run may start, for example so that only one replica of a service runs a job
/// at a time. Register one implementation as a singleton to apply it to every periodic job;
/// without one, every run starts.
/// </summary>
public interface IPeriodicJobGate
{
    /// <summary>
    /// Tries to let a run of the job start. Returns a lease that is disposed when the run ends, or
    /// <c>null</c> when the run must be skipped because another run holds the gate.
    /// </summary>
    Task<IAsyncDisposable?> TryEnter(string jobName, CancellationToken cancellationToken);
}
