namespace Pinqponq.Playground.Diagnostics;

/// <summary>
/// Ambient scenario-run id used to correlate log entries with the run that produced them.
/// </summary>
/// <remarks>
/// An <see cref="AsyncLocal{T}"/> rather than a logging scope, because a run may span
/// several logger factories (each run builds its own service provider) and even a
/// separate HTTP request into the error-handling sandbox, where the id is re-attached
/// from a request header.
/// </remarks>
public static class ScenarioRunContext
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The run currently executing on this async flow, if any.</summary>
    public static string? RunId => Current.Value;

    /// <summary>Attaches <paramref name="runId"/> until the returned scope is disposed.</summary>
    public static IDisposable Begin(string runId)
    {
        var previous = Current.Value;
        Current.Value = runId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = previous;
        }
    }
}
