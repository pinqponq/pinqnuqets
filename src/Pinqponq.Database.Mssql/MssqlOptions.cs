namespace Pinqponq.Database.Mssql;

/// <summary>
/// Configuration for the MSSQL connection layer.
/// </summary>
public sealed class MssqlOptions
{
    /// <summary>The SQL Server connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Maximum retry attempts on transient failures. Defaults to 3.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Base delay for exponential backoff between retries. Defaults to 200ms.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
