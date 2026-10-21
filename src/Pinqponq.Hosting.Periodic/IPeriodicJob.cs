namespace Pinqponq.Hosting.Periodic;

/// <summary>
/// The work a periodic job does on each run. The job is resolved from a new DI scope for every
/// run, so it can take scoped services (a database context, a repository) in its constructor.
/// </summary>
public interface IPeriodicJob
{
    /// <summary>Runs the job once.</summary>
    /// <param name="scopedServices">The services of the scope created for this run. The scope is disposed after the run.</param>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    Task Run(IServiceProvider scopedServices, CancellationToken cancellationToken);
}
