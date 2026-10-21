namespace Pinqponq.Hosting.Periodic;

/// <summary>Settings of one periodic job.</summary>
public sealed class PeriodicJobOptions
{
    /// <summary>
    /// The name used in log messages, observer calls and health check data. When it is not set,
    /// the job's type name is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>The time between the starts of two runs. Required: it must be greater than zero.</summary>
    public TimeSpan Interval { get; set; }

    /// <summary>The time the job waits after the host starts before its first run. Default: zero.</summary>
    public TimeSpan StartupDelay { get; set; }

    /// <summary>When <c>false</c>, the job never runs. Default: <c>true</c>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the first run starts right after <see cref="StartupDelay"/>. When
    /// <c>false</c>, the first run starts one <see cref="Interval"/> later. Default: <c>true</c>.
    /// </summary>
    public bool RunImmediately { get; set; } = true;
}
