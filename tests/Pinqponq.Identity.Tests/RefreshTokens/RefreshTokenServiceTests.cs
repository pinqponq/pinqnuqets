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
    public async Task Reuse_after_grace_revokes_entire_family_even_if_replacement_active()
    {
        var options = Options.Create(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromDays(1),
            TokenByteLength = 32,
            ReuseDetectionGrace = TimeSpan.FromMilliseconds(20),
        });
        var service = new RefreshTokenService(_store, options);

        var original = await service.IssueAsync("user-1");
        var rotated = await service.RotateAsync(original.Token);
        await Task.Delay(40);

        var act = () => service.RotateAsync(original.Token);
        await act.Should().ThrowAsync<InvalidRefreshTokenException>();

        var replacement = await _store.FindByHashAsync(rotated.Descriptor.TokenHash);
        replacement!.IsActive(DateTimeOffset.UtcNow).Should().BeFalse(
            "reuse after grace must revoke the active replacement");
    }

    [Fact]
    public async Task Presenting_rotated_token_within_grace_does_not_revoke_family()
    {
        var options = Options.Create(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromDays(1),
            TokenByteLength = 32,
            ReuseDetectionGrace = TimeSpan.FromSeconds(30),
        });
        var service = new RefreshTokenService(_store, options);

        var original = await service.IssueAsync("user-1");
        var rotated = await service.RotateAsync(original.Token);

        var act = () => service.RotateAsync(original.Token);
        await act.Should().ThrowAsync<InvalidRefreshTokenException>();

        var replacement = await _store.FindByHashAsync(rotated.Descriptor.TokenHash);
        replacement!.IsActive(DateTimeOffset.UtcNow).Should().BeTrue(
            "within grace, concurrent-safe path must not kill an unused replacement");
        _store.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task Reuse_of_rotated_token_after_descendant_used_revokes_entire_family()
    {
        var options = Options.Create(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromDays(1),
            TokenByteLength = 32,
            ReuseDetectionGrace = TimeSpan.Zero,
        });
        var service = new RefreshTokenService(_store, options);

        var original = await service.IssueAsync("user-1");
        var rotated = await service.RotateAsync(original.Token);
        var leaf = await service.RotateAsync(rotated.Token);

        var act = () => service.RotateAsync(original.Token);
        await act.Should().ThrowAsync<InvalidRefreshTokenException>();

        var leafEntity = await _store.FindByHashAsync(leaf.Descriptor.TokenHash);
        leafEntity!.IsActive(DateTimeOffset.UtcNow).Should().BeFalse(
            "reuse detection must revoke the current leaf (and the whole family)");
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

    [Fact]
    public async Task Concurrent_rotate_of_same_token_yields_single_active_replacement()
    {
        var original = await _service.IssueAsync("user-1");

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    return await _service.RotateAsync(original.Token);
                }
                catch (InvalidRefreshTokenException)
                {
                    return null;
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        results.Count(r => r is not null).Should().Be(1);
        _store.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task CompleteRotation_failure_revokes_family_and_leaves_no_active_orphan()
    {
        var original = await _service.IssueAsync("user-1");
        _store.FailAfterAddBeforeLink = true;
        _store.ThrowOnCompleteRotation = new InvalidOperationException("crash");

        var act = () => _service.RotateAsync(original.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();

        _store.ActiveCount.Should().Be(0, "failed rotation must revoke the subject family");
        var old = await _store.FindByHashAsync(original.Descriptor.TokenHash);
        old!.RevokedAt.Should().NotBeNull();
    }
}
