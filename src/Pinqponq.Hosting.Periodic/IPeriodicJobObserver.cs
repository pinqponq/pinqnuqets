namespace Pinqponq.Hosting.Periodic;

/// <summary>
/// Receives the result of every periodic job run, for example to forward it to an application's
/// own log service or metrics. Register implementations as singletons; none is registered by
/// default. An exception thrown by an observer is logged and does not change the run's result.
/// </summary>
public interface IPeriodicJobObserver
{
    /// <summary>Called right before a run starts.</summary>
    void OnStarted(string jobName);

    /// <summary>Called after a run finished without an exception.</summary>
    void OnSucceeded(string jobName, TimeSpan duration);

    /// <summary>Called after a run threw an exception. The job keeps its schedule.</summary>
    void OnFailed(string jobName, Exception exception, TimeSpan duration);
}
