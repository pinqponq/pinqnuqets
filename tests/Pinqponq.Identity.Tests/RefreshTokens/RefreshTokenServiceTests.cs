using FluentAssertions;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.RefreshTokens;
using Xunit;

namespace Pinqponq.Identity.Tests.RefreshTokens;

public sealed class RefreshTokenServiceTests
{
    private readonly InMemoryRefreshTokenStore _store = new();
    private readonly RefreshTokenService _service;

    public RefreshTokenServiceTests()
    {
        var options = Options.Create(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromDays(1),
            TokenByteLength = 32,
        });
        _service = new RefreshTokenService(_store, options);
    }

    [Fact]
    public async Task IssueAsync_returns_raw_token_and_stores_only_hash()
    {
        var result = await _service.IssueAsync("user-1");

        result.Token.Should().NotBeNullOrWhiteSpace();
        result.Descriptor.Subject.Should().Be("user-1");
        result.Descriptor.TokenHash.Should().NotBe(result.Token, "the raw token must not be stored");
        result.Descriptor.IsActive(DateTimeOffset.UtcNow).Should().BeTrue();
        _store.Count.Should().Be(1);
    }

    [Fact]
    public async Task RotateAsync_revokes_old_and_issues_linked_replacement()
    {
        var original = await _service.IssueAsync("user-1");

        var rotated = await _service.RotateAsync(original.Token);

        rotated.Token.Should().NotBe(original.Token);
        rotated.Descriptor.Subject.Should().Be("user-1");

        var oldRecord = await _store.FindByHashAsync(original.Descriptor.TokenHash);
        oldRecord!.RevokedAt.Should().NotBeNull();
        oldRecord.ReplacedByTokenHash.Should().Be(rotated.Descriptor.TokenHash);
        oldRecord.IsActive(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public async Task RotateAsync_with_revoked_token_throws()
    {
        var original = await _service.IssueAsync("user-1");
        await _service.RotateAsync(original.Token);

        var act = () => _service.RotateAsync(original.Token);

        await act.Should().ThrowAsync<InvalidRefreshTokenException>();
    }

    [Fact]
    public async Task RotateAsync_with_unknown_token_throws()
    {
        var act = () => _service.RotateAsync("does-not-exist");

        await act.Should().ThrowAsync<InvalidRefreshTokenException>();
    }

    [Fact]
    public async Task RevokeAsync_marks_token_inactive()
    {
        var original = await _service.IssueAsync("user-1");

        await _service.RevokeAsync(original.Token);

        var record = await _store.FindByHashAsync(original.Descriptor.TokenHash);
        record!.IsActive(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public async Task Expired_token_is_not_active_and_cannot_rotate()
    {
        var options = Options.Create(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromMilliseconds(1),
        });
        var service = new RefreshTokenService(_store, options);

        var issued = await service.IssueAsync("user-1");
        await Task.Delay(20);

        issued.Descriptor.IsActive(DateTimeOffset.UtcNow).Should().BeFalse();
        await FluentActions.Awaiting(() => service.RotateAsync(issued.Token))
            .Should().ThrowAsync<InvalidRefreshTokenException>();
    }
}
