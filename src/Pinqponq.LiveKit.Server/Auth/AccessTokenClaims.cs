using System.Text.Json.Serialization;

namespace Pinqponq.LiveKit.Server.Auth;

internal sealed class AccessTokenClaims
{
    [JsonPropertyName("iss")]
    public string? Issuer { get; init; }

    [JsonPropertyName("sub")]
    public string? Subject { get; init; }

    [JsonPropertyName("iat")]
    public long? IssuedAt { get; init; }

    [JsonPropertyName("nbf")]
    public long? NotBefore { get; init; }

    [JsonPropertyName("exp")]
    public long? ExpiresAt { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("metadata")]
    public string? Metadata { get; init; }

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }

    [JsonPropertyName("video")]
    public VideoGrant? Video { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }
}
