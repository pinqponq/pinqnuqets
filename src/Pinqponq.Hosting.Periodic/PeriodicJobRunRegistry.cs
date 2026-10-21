using System.Collections.Concurrent;

namespace Pinqponq.Hosting.Periodic;

internal sealed record PeriodicJobRunStatus(
    bool IsEnabled,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt);

internal sealed class PeriodicJobRunRegistry
{
    private readonly ConcurrentDictionary<string, PeriodicJobRunStatus> _statuses = new();

    public void RecordStarted(string jobKey, bool isEnabled, DateTimeOffset startedAt) =>
        _statuses[jobKey] = new PeriodicJobRunStatus(isEnabled, startedAt, LastSuccessAt: null, LastFailureAt: null);

    public void RecordSuccess(string jobKey, DateTimeOffset finishedAt) =>
        _statuses[jobKey] = _statuses[jobKey] with { LastSuccessAt = finishedAt };

    public void RecordFailure(string jobKey, DateTimeOffset finishedAt) =>
        _statuses[jobKey] = _statuses[jobKey] with { LastFailureAt = finishedAt };

    public PeriodicJobRunStatus? Find(string jobKey) =>
        _statuses.TryGetValue(jobKey, out var status) ? status : null;
}
