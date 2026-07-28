using System.Threading.Channels;

namespace Pinqponq.Playground.Diagnostics;

/// <summary>
/// Shared destination for every captured log entry: a bounded ring buffer for history
/// plus a fan-out to live subscribers.
/// </summary>
public sealed class LogSink
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Queue<LogRecord> _buffer;
    private readonly List<Channel<LogRecord>> _subscribers = [];

    private long _nextId;

    /// <summary>Creates the sink with a bounded history.</summary>
    public LogSink(int capacity = 5000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _buffer = new Queue<LogRecord>(capacity);
    }

    /// <summary>Number of entries currently retained.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _buffer.Count;
            }
        }
    }

    /// <summary>Assigns the next id, appends to history and notifies subscribers.</summary>
    public void Write(Func<long, LogRecord> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        LogRecord record;
        Channel<LogRecord>[] targets;

        lock (_gate)
        {
            record = factory(++_nextId);
            _buffer.Enqueue(record);
            while (_buffer.Count > _capacity)
            {
                _buffer.Dequeue();
            }

            targets = [.. _subscribers];
        }

        // Publishing happens outside the lock: a slow reader must never stall logging.
        foreach (var channel in targets)
        {
            channel.Writer.TryWrite(record);
        }
    }

    /// <summary>Returns matching history, oldest first.</summary>
    public IReadOnlyList<LogRecord> Query(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        LogRecord[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _buffer];
        }

        IEnumerable<LogRecord> result = snapshot;

        if (query.SinceId is { } sinceId)
        {
            result = result.Where(r => r.Id > sinceId);
        }

        if (query.MinLevel is { } minLevel)
        {
            result = result.Where(r => LevelRank(r.Level) >= LevelRank(minLevel));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            result = result.Where(r =>
                r.Category.Contains(query.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.RunId))
        {
            result = result.Where(r => string.Equals(r.RunId, query.RunId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            result = result.Where(r =>
                r.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || r.Category.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || (r.Exception?.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Newest entries matter most, but the client renders oldest-first.
        return [.. result.TakeLast(query.Take)];
    }

    /// <summary>Clears the retained history. Live subscribers are unaffected.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    /// <summary>Subscribes to new entries. Dispose the subscription to stop receiving.</summary>
    public LogSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        return new LogSubscription(channel.Reader, () =>
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        });
    }

    private static int LevelRank(string level) => level switch
    {
        "Trace" => 0,
        "Debug" => 1,
        "Information" => 2,
        "Warning" => 3,
        "Error" => 4,
        "Critical" => 5,
        _ => 2,
    };
}

/// <summary>Filter for <see cref="LogSink.Query"/>.</summary>
public sealed record LogQuery
{
    public string? MinLevel { get; init; }

    public string? Category { get; init; }

    public string? Search { get; init; }

    public string? RunId { get; init; }

    public long? SinceId { get; init; }

    public int Take { get; init; } = 500;
}

/// <summary>A live log subscription; dispose to unsubscribe.</summary>
public sealed class LogSubscription(ChannelReader<LogRecord> reader, Action onDispose) : IDisposable
{
    private bool _disposed;

    /// <summary>Reader delivering entries as they are written.</summary>
    public ChannelReader<LogRecord> Reader { get; } = reader;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        onDispose();
    }
}
