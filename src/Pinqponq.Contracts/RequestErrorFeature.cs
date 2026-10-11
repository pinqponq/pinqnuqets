namespace Pinqponq.Contracts;

/// <summary>Default <see cref="IRequestErrorFeature"/>.</summary>
/// <param name="EventName">Log event name.</param>
/// <param name="Detail">Structured error detail.</param>
public sealed record RequestErrorFeature(string EventName, IReadOnlyDictionary<string, object?> Detail)
    : IRequestErrorFeature;
