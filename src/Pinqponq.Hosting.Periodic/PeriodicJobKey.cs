namespace Pinqponq.Hosting.Periodic;

internal static class PeriodicJobKey
{
    public static string For<TJob>()
        where TJob : class, IPeriodicJob =>
        typeof(TJob).FullName ?? typeof(TJob).Name;

    public static string ResolveName<TJob>(PeriodicJobOptions options)
        where TJob : class, IPeriodicJob =>
        options.Name ?? typeof(TJob).Name;
}
