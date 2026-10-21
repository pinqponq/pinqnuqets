namespace Pinqponq.Hosting.Periodic;

internal sealed class PeriodicJobRegistration<TJob>
    where TJob : class, IPeriodicJob;
