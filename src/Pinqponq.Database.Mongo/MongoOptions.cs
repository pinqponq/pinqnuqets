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
}
