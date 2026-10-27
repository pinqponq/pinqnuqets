namespace Pinqponq.Push.DeviceTokens;

/// <summary>One push token of one device, owned by one user.</summary>
public sealed class DeviceToken
{
    public long Id { get; set; }

    /// <summary>The user the token belongs to, as the application identifies users.</summary>
    public required string UserId { get; set; }

    /// <summary>The token the push provider issued. Unique: a token addresses one device.</summary>
    public required string Token { get; set; }

    public required string Platform { get; set; }

    /// <summary>
    /// Stable identifier of the physical device, supplied by the client. A user holds one row per device,
    /// so a token rotation updates that row instead of leaving the dead token behind. <see langword="null"/>
    /// for clients that do not send one; they match on the token alone.
    /// </summary>
    public string? DeviceIdentifier { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
