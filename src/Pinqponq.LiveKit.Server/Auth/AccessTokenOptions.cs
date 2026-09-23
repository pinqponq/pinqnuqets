namespace Pinqponq.LiveKit.Server.Auth;

public sealed class AccessTokenOptions
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    /// <summary>
    /// Participant identity (JWT <c>sub</c>). Required when the grant allows joining a room.
    /// </summary>
    public string? Identity { get; init; }

    public string? Name { get; init; }
    public string? Metadata { get; init; }
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
    public VideoGrant? VideoGrant { get; init; }
    public TimeSpan Ttl { get; init; } = DefaultTtl;
}
