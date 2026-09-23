using FluentAssertions;
using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.Models;
using System.Text.Json;
using Xunit;
using Reference = Livekit.Server.Sdk.Dotnet;

namespace Pinqponq.LiveKit.Server.Tests;

public sealed class AccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AccessTokenOptions CreateCallTokenOptions() => new()
    {
        Identity = "user-1",
        Name = "User One",
        Metadata = """{"callId":"c1"}""",
        Ttl = TimeSpan.FromSeconds(120),
        VideoGrant = new VideoGrant
        {
            RoomJoin = true,
            Room = "project-1-call-c1",
            CanPublish = true,
            CanSubscribe = true,
            CanPublishData = false,
            CanPublishSources = [TrackSource.Microphone, TrackSource.Camera]
        }
    };

    [Fact]
    public async Task CreateToken_WritesLiveKitClaimLayout()
    {
        var issuer = new AccessTokenIssuer(TestCredentials.Provider, new FixedTimeProvider(Now));

        var token = await issuer.CreateToken(CreateCallTokenOptions());
        var payload = JwtPayload.Decode(token.Value);
        var video = payload.GetProperty("video");

        payload.GetProperty("iss").GetString().Should().Be(TestCredentials.API_KEY);
        payload.GetProperty("sub").GetString().Should().Be("user-1");
        payload.GetProperty("name").GetString().Should().Be("User One");
        payload.GetProperty("metadata").GetString().Should().Be("""{"callId":"c1"}""");
        (payload.GetProperty("exp").GetInt64() - payload.GetProperty("nbf").GetInt64()).Should().Be(120);
        payload.GetProperty("exp").GetInt64().Should().Be(token.ExpiresAt.ToUnixTimeSeconds());

        video.GetProperty("roomJoin").GetBoolean().Should().BeTrue();
        video.GetProperty("room").GetString().Should().Be("project-1-call-c1");
        video.GetProperty("canPublishData").GetBoolean().Should().BeFalse();
        video.GetProperty("canPublishSources").EnumerateArray().Select(source => source.GetString())
            .Should().Equal("microphone", "camera");
        video.TryGetProperty("roomCreate", out _).Should().BeFalse();
        video.TryGetProperty("roomAdmin", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateToken_IsAcceptedByReferenceSdkVerifier()
    {
        var issuer = new AccessTokenIssuer(TestCredentials.Provider);
        var token = await issuer.CreateToken(CreateCallTokenOptions());

        var verifier = new Reference.TokenVerifier(TestCredentials.API_KEY, TestCredentials.API_SECRET);
        var claims = verifier.Verify(token.Value);

        claims.Identity.Should().Be("user-1");
        claims.Video.Room.Should().Be("project-1-call-c1");
        claims.Video.CanPublishSources.Should().Equal("microphone", "camera");
    }

    [Fact]
    public void Verify_AcceptsTokenIssuedByReferenceSdk()
    {
        var referenceGrant = new Reference.VideoGrants { RoomJoin = true, Room = "room-1" };
        var referenceToken = new Reference.AccessToken(TestCredentials.API_KEY, TestCredentials.API_SECRET)
            .WithIdentity("user-2")
            .WithTtl(TimeSpan.FromMinutes(5))
            .WithGrants(referenceGrant)
            .ToJwt();

        var claims = JsonWebToken.Verify(referenceToken, TestCredentials.Credentials, DateTimeOffset.UtcNow);

        claims.Subject.Should().Be("user-2");
        (claims.Video?.Room).Should().Be("room-1");
        (claims.Video?.RoomJoin).Should().BeTrue();
    }

    [Fact]
    public async Task CreateToken_WithoutIdentityForJoinGrant_Throws()
    {
        var issuer = new AccessTokenIssuer(TestCredentials.Provider);
        var tokenOptions = new AccessTokenOptions { VideoGrant = new VideoGrant { RoomJoin = true, Room = "room-1" } };

        var act = () => issuer.CreateToken(tokenOptions);

        await act.Should().ThrowExactlyAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateToken_WithUngrantableSource_Throws()
    {
        var issuer = new AccessTokenIssuer(TestCredentials.Provider);
        var tokenOptions = new AccessTokenOptions
        {
            Identity = "user-1",
            VideoGrant = new VideoGrant { RoomJoin = true, Room = "room-1", CanPublishSources = [TrackSource.Unknown] }
        };

        var act = () => issuer.CreateToken(tokenOptions);

        await act.Should().ThrowExactlyAsync<ArgumentException>();
    }

    [Fact]
    public void Verify_RejectsTamperedAndForeignTokens()
    {
        var token = AccessTokenIssuer.Issue(CreateCallTokenOptions(), TestCredentials.Credentials, Now).Value;
        var otherCredentials = new LiveKitCredentials("APIother", TestCredentials.API_SECRET);
        var wrongSecretCredentials = new LiveKitCredentials(TestCredentials.API_KEY, "another-secret-with-enough-length-000");
        var unsignedToken = $"{Base64UrlJson("""{"alg":"none"}""")}.{token.Split('.')[1]}.";

        var verifyWithOtherApiKey = () => JsonWebToken.Verify(token, otherCredentials, Now);
        var verifyWithWrongSecret = () => JsonWebToken.Verify(token, wrongSecretCredentials, Now);
        var verifyUnsignedToken = () => JsonWebToken.Verify(unsignedToken, TestCredentials.Credentials, Now);
        var verifyAfterExpiry = () => JsonWebToken.Verify(token, TestCredentials.Credentials, Now.AddMinutes(10));
        var verifyNonJwt = () => JsonWebToken.Verify("not-a-jwt", TestCredentials.Credentials, Now);

        verifyWithOtherApiKey.Should().ThrowExactly<InvalidAccessTokenException>();
        verifyWithWrongSecret.Should().ThrowExactly<InvalidAccessTokenException>();
        verifyUnsignedToken.Should().ThrowExactly<InvalidAccessTokenException>();
        verifyAfterExpiry.Should().ThrowExactly<InvalidAccessTokenException>();
        verifyNonJwt.Should().ThrowExactly<InvalidAccessTokenException>();
    }

    [Fact]
    public void Verify_AllowsOneMinuteClockSkew()
    {
        var token = AccessTokenIssuer.Issue(CreateCallTokenOptions(), TestCredentials.Credentials, Now).Value;

        var verifyBeforeNotBefore = () => JsonWebToken.Verify(token, TestCredentials.Credentials, Now.AddSeconds(-50));
        var verifyAfterExpiry = () => JsonWebToken.Verify(token, TestCredentials.Credentials, Now.AddSeconds(170));

        verifyBeforeNotBefore.Should().NotThrow();
        verifyAfterExpiry.Should().NotThrow();
    }

    private static string Base64UrlJson(string json) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(json));
}
