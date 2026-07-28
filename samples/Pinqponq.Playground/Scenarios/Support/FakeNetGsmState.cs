using System.Collections.Concurrent;

namespace Pinqponq.Playground.Scenarios.Support;

/// <summary>
/// Backs the console's stand-in for the NetGSM HTTP API.
/// </summary>
/// <remarks>
/// There is no free NetGSM endpoint to send test traffic to, and a mocked
/// <see cref="HttpMessageHandler"/> would bypass the very thing worth checking — the
/// named <see cref="HttpClient"/>, the query construction and the Polly pipeline. Hosting
/// a fake endpoint in-process exercises all of it for real, and lets a scenario inject
/// failures to prove the retry actually retries.
/// </remarks>
public sealed class FakeNetGsmState
{
    private readonly ConcurrentQueue<FakeSmsRequest> _requests = new();
    private readonly object _gate = new();

    private int _failuresRemaining;
    private long _sequence;
    private DateTimeOffset? _lastReceivedAt;

    /// <summary>Requests the fake endpoint has received, newest last.</summary>
    public IReadOnlyList<FakeSmsRequest> Requests => [.. _requests];

    /// <summary>How many upcoming requests will be answered with 500.</summary>
    public int FailuresRemaining
    {
        get
        {
            lock (_gate)
            {
                return _failuresRemaining;
            }
        }
    }

    /// <summary>Makes the next <paramref name="count"/> requests fail with HTTP 500.</summary>
    public void FailNext(int count)
    {
        lock (_gate)
        {
            _failuresRemaining = Math.Max(0, count);
        }
    }

    /// <summary>Records a request and decides how to answer it.</summary>
    public FakeSmsRequest Record(
        string? userCode,
        string? gsmNo,
        string? message,
        string? msgHeader,
        string rawQuery,
        string? runId)
    {
        bool fail;
        long sequence;
        long? deltaMs;
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            fail = _failuresRemaining > 0;
            if (fail)
            {
                _failuresRemaining--;
            }

            sequence = ++_sequence;
            deltaMs = _lastReceivedAt is { } previous
                ? (long)(now - previous).TotalMilliseconds
                : null;
            _lastReceivedAt = now;
        }

        var record = new FakeSmsRequest(
            sequence,
            now,
            deltaMs,
            gsmNo,
            message,
            msgHeader,
            userCode,
            rawQuery,
            fail ? 500 : 200,
            runId);

        _requests.Enqueue(record);
        while (_requests.Count > 200 && _requests.TryDequeue(out _))
        {
            // Bounded history; the console only ever shows the recent tail.
        }

        return record;
    }

    /// <summary>Clears recorded requests and any pending failure injection.</summary>
    public void Reset()
    {
        while (_requests.TryDequeue(out _))
        {
            // drain
        }

        lock (_gate)
        {
            _failuresRemaining = 0;
            _sequence = 0;
            _lastReceivedAt = null;
        }
    }
}

/// <summary>One request the fake NetGSM endpoint received.</summary>
/// <param name="DeltaMs">Milliseconds since the previous request — the retry backoff.</param>
public sealed record FakeSmsRequest(
    long Sequence,
    DateTimeOffset ReceivedAt,
    long? DeltaMs,
    string? GsmNo,
    string? Message,
    string? MsgHeader,
    string? UserCode,
    string RawQuery,
    int ResponseStatus,
    string? RunId);
