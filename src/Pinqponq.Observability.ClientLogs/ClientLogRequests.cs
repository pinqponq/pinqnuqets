namespace Pinqponq.Observability.ClientLogs;

internal sealed class ClientLogEntryRequest
{
    public string? Event { get; set; }

    public string? LogLevel { get; set; }

    public Dictionary<string, string?>? Metadata { get; set; }

    public Dictionary<string, string?>? Detail { get; set; }

    public DateTimeOffset? Date { get; set; }
}

// pinqloq-kmp also sends "collectionName"; it is not bound, because the server chooses the collection.
internal sealed class ClientLogBatchRequest
{
    public List<ClientLogEntryRequest?>? Logs { get; set; }
}
