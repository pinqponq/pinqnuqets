namespace Pinqponq.LiveKit.Server.Auth;

public sealed class AccessTokenIssuer
{
    private readonly ILiveKitCredentialsProvider _credentialsProvider;
    private readonly TimeProvider _timeProvider;

    public AccessTokenIssuer(ILiveKitCredentialsProvider credentialsProvider, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(credentialsProvider);

        _credentialsProvider = credentialsProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LiveKitAccessToken> CreateToken(AccessTokenOptions tokenOptions, CancellationToken cancellationToken = default)
    {
        var credentials = await _credentialsProvider.GetCredentials(cancellationToken);
        return Issue(tokenOptions, credentials, _timeProvider.GetUtcNow());
    }

    internal static LiveKitAccessToken Issue(AccessTokenOptions tokenOptions, LiveKitCredentials credentials, DateTimeOffset now)
    {
        ValidateTokenOptions(tokenOptions);

        var issuedAt = now.ToUnixTimeSeconds();
        var expiresAt = now.Add(tokenOptions.Ttl).ToUnixTimeSeconds();
        var claims = new AccessTokenClaims
        {
            Issuer = credentials.ApiKey,
            Subject = tokenOptions.Identity,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            ExpiresAt = expiresAt,
            Name = tokenOptions.Name,
            Metadata = tokenOptions.Metadata,
            Attributes = tokenOptions.Attributes,
            Video = tokenOptions.VideoGrant
        };

        var tokenValue = JsonWebToken.Sign(claims, credentials.ApiSecret);
        var expirationTime = DateTimeOffset.FromUnixTimeSeconds(expiresAt);
        return new LiveKitAccessToken
        {
            Value = tokenValue,
            ExpiresAt = expirationTime
        };
    }

    private static void ValidateTokenOptions(AccessTokenOptions tokenOptions)
    {
        ArgumentNullException.ThrowIfNull(tokenOptions);

        if (tokenOptions.Ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenOptions), tokenOptions.Ttl, "Token TTL must be positive.");
        }

        var videoGrant = tokenOptions.VideoGrant;
        if (videoGrant is null)
        {
            return;
        }

        if (videoGrant.RoomJoin && string.IsNullOrWhiteSpace(tokenOptions.Identity))
        {
            throw new ArgumentException("Identity is required when the grant allows joining a room.", nameof(tokenOptions));
        }

        var requiresRoom = videoGrant.RoomJoin || videoGrant.RoomAdmin;
        if (requiresRoom && string.IsNullOrWhiteSpace(videoGrant.Room))
        {
            throw new ArgumentException("Room is required when the grant allows joining or administering a room.", nameof(tokenOptions));
        }
    }
}
