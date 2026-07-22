namespace Pinqponq.Database.Mongo;

/// <summary>
/// Configuration for the MongoDB connection layer.
/// </summary>
public sealed class MongoOptions
{
    /// <summary>The MongoDB connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>The default database name resolved into <c>IMongoDatabase</c>.</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Maximum retry attempts for the health-check ping. Defaults to 3.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Base delay for exponential backoff between retries. Defaults to 200ms.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
