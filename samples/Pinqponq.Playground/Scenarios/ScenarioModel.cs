using Pinqponq.Playground.Diagnostics;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Input control the console renders for a scenario field.</summary>
public enum ScenarioFieldKind
{
    Text,
    MultilineText,
    Password,
    Number,
    Bool,
    Enum,
    Duration,
}

/// <summary>One editable input — either a package option or scenario data.</summary>
public sealed record ScenarioField(
    string Name,
    string Label,
    ScenarioFieldKind Kind,
    string? Default = null,
    string? Help = null,
    IReadOnlyList<string>? Choices = null,
    bool Required = false);

/// <summary>Everything the console needs to render and launch a scenario.</summary>
public sealed record ScenarioDescriptor
{
    public required string Id { get; init; }

    /// <summary>Owning package, e.g. <c>Pinqponq.Cache</c>.</summary>
    public required string PackageId { get; init; }

    public required string Title { get; init; }

    /// <summary>Turkish description of what the scenario proves.</summary>
    public required string Summary { get; init; }

    /// <summary>Dev-stack service ids that must be ready before running.</summary>
    public IReadOnlyList<string> RequiredServices { get; init; } = [];

    public IReadOnlyList<ScenarioField> Fields { get; init; } = [];

    /// <summary>Marks scenarios whose point is that the call fails in a specific way.</summary>
    public bool NegativePath { get; init; }

    /// <summary>Marks scenarios that need outbound internet access.</summary>
    public bool NeedsInternet { get; init; }

    public int TimeoutSeconds { get; init; } = 60;
}

/// <summary>A runnable scenario: its descriptor plus the body that exercises the package.</summary>
public sealed class Scenario(ScenarioDescriptor descriptor, Func<ScenarioContext, Task> run)
{
    public ScenarioDescriptor Descriptor { get; } = descriptor;

    /// <summary>Executes the scenario body.</summary>
    public Task RunAsync(ScenarioContext context) => run(context);
}

/// <summary>One recorded step within a run.</summary>
public sealed record ScenarioStep(int Index, string Title, bool Ok, string? Detail, long ElapsedMs);

/// <summary>A value produced by a run and rendered in the console.</summary>
/// <param name="Kind">One of <c>json</c>, <c>text</c>, <c>token</c>, <c>table</c>, <c>uri</c>.</param>
public sealed record ScenarioArtifact(string Name, string Kind, object? Value);

/// <summary>Outcome of a single scenario run, including the logs it produced.</summary>
public sealed record ScenarioRunResult
{
    public required string RunId { get; init; }

    public required string ScenarioId { get; init; }

    public required bool Success { get; init; }

    /// <summary>One of <c>passed</c>, <c>failed</c> or <c>skipped</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Failure summary shown to the user, in Turkish where the console raised it.</summary>
    public string? Error { get; init; }

    public string? ErrorType { get; init; }

    public required long DurationMs { get; init; }

    public IReadOnlyList<ScenarioStep> Steps { get; init; } = [];

    public IReadOnlyList<ScenarioArtifact> Artifacts { get; init; } = [];

    /// <summary>Every log entry emitted while this run was executing.</summary>
    public IReadOnlyList<LogRecord> Logs { get; init; } = [];
}
