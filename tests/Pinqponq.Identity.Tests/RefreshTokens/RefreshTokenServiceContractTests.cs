using Microsoft.Extensions.Options;
using Pinqponq.Identity.RefreshTokens;
using Xunit;

namespace Pinqponq.Identity.Tests.RefreshTokens;

/// <summary>
/// <see cref="RefreshTokenService"/> scenarios that every <see cref="IRefreshTokenStore"/> must
/// pass. A derived class supplies the store; the EF Core store's test project compiles this file
/// too, so both stores run the same scenarios.
/// </summary>
public abstract class RefreshTokenServiceContractTests
{
    private static readonly RefreshTokenOptions DefaultOptions = new()
    {
        Lifetime = TimeSpan.FromDays(1),
        TokenByteLength = 32,
    };

    /// <summary>
    /// Returns a store over the shared test storage. A store that is not thread-safe returns a
    /// new instance on every call, so concurrent scenarios can use one store per request.
    /// </summary>
    protected abstract IRefreshTokenStore CreateStore();

    protected abstract Task<int> CountStoredTokens();

    protected abstract Task<int> CountActiveTokens();

    /// <summary>Makes the next <see cref="IRefreshTokenStore.CompleteRotationAsync"/> throw.</summary>
    protected abstract void FailNextRotation(Exception failure);

    private RefreshTokenService CreateService(RefreshTokenOptions? options = null) =>
        new(CreateStore(), Options.Create(options ?? DefaultOptions));

    private Task<RefreshToken?> Find(string tokenHash) => CreateStore().FindByHashAsync(tokenHash);

    [Fact]
    public async Task IssueAsync_returns_raw_token_and_stores_only_hash()
    {
        var result = await CreateService().IssueAsync("user-1");

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("user-1", result.Descriptor.Subject);
        Assert.True(result.Descriptor.TokenHash != result.Token, "the raw token must not be stored");
        Assert.True(result.Descriptor.IsActive(DateTimeOffset.UtcNow));
        Assert.Equal(1, await CountStoredTokens());
    }

    [Fact]
    public async Task RotateAsync_revokes_old_and_issues_linked_replacement()
    {
        var service = CreateService();
        var original = await service.IssueAsync("user-1");

        var rotated = await service.RotateAsync(original.Token);

        Assert.NotEqual(original.Token, rotated.Token);
        Assert.Equal("user-1", rotated.Descriptor.Subject);

        var oldRecord = await Find(original.Descriptor.TokenHash);
        Assert.NotNull(oldRecord!.RevokedAt);
        Assert.Equal(rotated.Descriptor.TokenHash, oldRecord.ReplacedByTokenHash);
        Assert.False(oldRecord.IsActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RotateAsync_with_revoked_token_throws()
    {
        var service = CreateService();
        var original = await service.IssueAsync("user-1");
        await service.RotateAsync(original.Token);

        var act = () => service.RotateAsync(original.Token);

        await Assert.ThrowsAnyAsync<InvalidRefreshTokenException>(act);
    }

    [Fact]
    public async Task Reuse_after_grace_revokes_entire_family_even_if_replacement_active()
    {
        var service = CreateService(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromDays(1),
            TokenByteLength = 32,
            ReuseDetectionGrace = TimeSpan.FromMilliseconds(20),
        });

        var original = await service.IssueAsync("user-1");
        var rotated = await service.RotateAsync(original.Token);
        await Task.Delay(40);

        var act = () => service.RotateAsync(original.Token);
        await Assert.ThrowsAnyAsync<InvalidRefreshTokenException>(act);

        var replacement = await Find(rotated.Descriptor.TokenHash);
        Assert.False(
            replacement!.IsActive(DateTimeOffset.UtcNow),
            "reuse after grace must revoke the active replacement");
    }

    [Fact]
    public async Task Presenting_rotated_token_within_grace_does_not_revoke_family()
    {
        var service = CreateService(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromDays(1),
            TokenByteLength = 32,
            ReuseDetectionGrace = TimeSpan.FromSeconds(30),
        });

        var original = await service.IssueAsync("user-1");
        var rotated = await service.RotateAsync(original.Token);

        var act = () => service.RotateAsync(original.Token);
        await Assert.ThrowsAnyAsync<InvalidRefreshTokenException>(act);

        var replacement = await Find(rotated.Descriptor.TokenHash);
        Assert.True(
            replacement!.IsActive(DateTimeOffset.UtcNow),
            "within grace, concurrent-safe path must not kill an unused replacement");
        Assert.Equal(1, await CountActiveTokens());
    }

    [Fact]
    public async Task Reuse_of_rotated_token_after_descendant_used_revokes_entire_family()
    {
        var service = CreateService(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromDays(1),
            TokenByteLength = 32,
            ReuseDetectionGrace = TimeSpan.Zero,
        });

        var original = await service.IssueAsync("user-1");
        var rotated = await service.RotateAsync(original.Token);
        var leaf = await service.RotateAsync(rotated.Token);

        var act = () => service.RotateAsync(original.Token);
        await Assert.ThrowsAnyAsync<InvalidRefreshTokenException>(act);

        var leafEntity = await Find(leaf.Descriptor.TokenHash);
        Assert.False(
            leafEntity!.IsActive(DateTimeOffset.UtcNow),
            "reuse detection must revoke the current leaf (and the whole family)");
    }

    [Fact]
    public async Task RotateAsync_with_unknown_token_throws()
    {
        var act = () => CreateService().RotateAsync("does-not-exist");

        await Assert.ThrowsAnyAsync<InvalidRefreshTokenException>(act);
    }

    [Fact]
    public async Task RevokeAsync_marks_token_inactive()
    {
        var service = CreateService();
        var original = await service.IssueAsync("user-1");

        await service.RevokeAsync(original.Token);

        var record = await Find(original.Descriptor.TokenHash);
        Assert.False(record!.IsActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Expired_token_is_not_active_and_cannot_rotate()
    {
        var service = CreateService(new RefreshTokenOptions
        {
            Lifetime = TimeSpan.FromMilliseconds(1),
        });

        var issued = await service.IssueAsync("user-1");
        await Task.Delay(20);

        Assert.False(issued.Descriptor.IsActive(DateTimeOffset.UtcNow));
        await Assert.ThrowsAnyAsync<InvalidRefreshTokenException>(() => service.RotateAsync(issued.Token));
    }

    [Fact]
    public async Task Concurrent_rotate_of_same_token_yields_single_active_replacement()
    {
        var original = await CreateService().IssueAsync("user-1");

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => CreateService())
            .Select(service => Task.Run(async () =>
            {
                try
                {
                    return await service.RotateAsync(original.Token);
                }
                catch (InvalidRefreshTokenException)
                {
                    return null;
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(r => r is not null));
        Assert.Equal(1, await CountActiveTokens());
    }

    [Fact]
    public async Task CompleteRotation_failure_revokes_family_and_leaves_no_active_orphan()
    {
        var service = CreateService();
        var original = await service.IssueAsync("user-1");
        FailNextRotation(new InvalidOperationException("crash"));

        var act = () => service.RotateAsync(original.Token);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(act);

        Assert.True(await CountActiveTokens() == 0, "failed rotation must revoke the subject family");
        var old = await Find(original.Descriptor.TokenHash);
        Assert.NotNull(old!.RevokedAt);
    }
}
