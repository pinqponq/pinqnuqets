namespace Pinqponq.LiveKit.Server.Auth;

public sealed class LiveKitAccessToken
{
    public required string Value { get; init; }

    /// <summary>
    /// Same instant as the token's <c>exp</c> claim.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
