using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pinqponq.Identity.RefreshTokens;
using Xunit;

namespace Pinqponq.Identity.EntityFrameworkCore.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OwnedRecordStoreTests(PostgresCollectionFixture fixture)
    : EntityFrameworkRefreshTokenStoreTests<OwnedRefreshTokenRecord>(fixture)
{
    [Fact]
    public async Task OnRecordCreated_sets_the_application_columns_that_are_saved()
    {
        StoreOptions.OnRecordCreated = (refreshToken, record) => record.OwnerId = int.Parse(refreshToken.Subject);
        var token = CreateToken("hash-1");
        token.Subject = "42";

        await CreateStore().AddAsync(token);

        var record = await Database.CreateContext().Set<OwnedRefreshTokenRecord>().SingleAsync();
        Assert.Equal(42, record.OwnerId);
    }
}

/// <summary>
/// Every column of this record has a name that differs from its property, so these runs prove
/// that the store's queries go through the application's column mapping.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RenamedColumnRecordStoreTests(PostgresCollectionFixture fixture)
    : EntityFrameworkRefreshTokenStoreTests<RenamedColumnRefreshTokenRecord>(fixture);

public abstract class EntityFrameworkRefreshTokenStoreTests<TRecord> : IAsyncLifetime
    where TRecord : class, IRefreshTokenRecord, new()
{
    protected static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly PostgresCollectionFixture _fixture;
    private readonly FakeTimeProvider _timeProvider = new(Now);

    protected EntityFrameworkRefreshTokenStoreTests(PostgresCollectionFixture fixture) => _fixture = fixture;

    protected TestDatabase Database { get; private set; } = null!;

    protected EntityFrameworkRefreshTokenStoreOptions<TRecord> StoreOptions { get; } = new();

    public async Task InitializeAsync() => Database = await TestDatabase.CreateAsync(_fixture);

    public async Task DisposeAsync() => await Database.DisposeAsync();

    [Fact]
    public async Task AddAsync_stores_a_token_that_FindByHashAsync_returns()
    {
        var token = CreateToken("hash-1");
        token.RevokedAt = Now.AddMinutes(1);
        token.ReplacedByTokenHash = "hash-2";

        await CreateStore().AddAsync(token);
        var storedToken = await Find("hash-1");

        Assert.NotNull(storedToken);
        Assert.Equal("user-1", storedToken.Subject);
        Assert.Equal("hash-1", storedToken.TokenHash);
        Assert.Equal(Now, storedToken.CreatedAt);
        Assert.Equal(Now.AddDays(30), storedToken.ExpiresAt);
        Assert.Equal(Now.AddMinutes(1), storedToken.RevokedAt);
        Assert.Equal("hash-2", storedToken.ReplacedByTokenHash);
    }

    [Fact]
    public async Task AddAsync_stores_times_with_an_offset_as_the_same_instant_in_utc()
    {
        var offset = TimeSpan.FromHours(3);
        var token = CreateToken("hash-1");
        token.CreatedAt = Now.ToOffset(offset);
        token.ExpiresAt = Now.AddDays(30).ToOffset(offset);
        token.RevokedAt = Now.AddMinutes(1).ToOffset(offset);

        await CreateStore().AddAsync(token);
        var storedToken = await Find("hash-1");

        Assert.Equal(Now, storedToken!.CreatedAt);
        Assert.Equal(TimeSpan.Zero, storedToken.CreatedAt.Offset);
        Assert.Equal(Now.AddDays(30), storedToken.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, storedToken.ExpiresAt.Offset);
        Assert.Equal(Now.AddMinutes(1), storedToken.RevokedAt);
        Assert.Equal(TimeSpan.Zero, storedToken.RevokedAt!.Value.Offset);
    }

    [Fact]
    public async Task FindByHashAsync_returns_null_for_an_unknown_hash()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));

        Assert.Null(await Find("unknown"));
    }

    [Fact]
    public async Task FindByHashAsync_does_not_track_the_record()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var dbContext = Database.CreateContext();
        var refreshTokenStore = new EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, TRecord>(
            dbContext,
            Microsoft.Extensions.Options.Options.Create(StoreOptions),
            _timeProvider);

        await refreshTokenStore.FindByHashAsync("hash-1");

        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UpdateAsync_saves_the_token_values()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var changedToken = CreateToken("hash-1");
        changedToken.Subject = "user-2";
        changedToken.ExpiresAt = Now.AddDays(7);
        changedToken.RevokedAt = Now.AddMinutes(1);
        changedToken.ReplacedByTokenHash = "hash-2";

        await CreateStore().UpdateAsync(changedToken);
        var storedToken = await Find("hash-1");

        Assert.Equal("user-2", storedToken!.Subject);
        Assert.Equal(Now, storedToken.CreatedAt);
        Assert.Equal(Now.AddDays(7), storedToken.ExpiresAt);
        Assert.Equal(Now.AddMinutes(1), storedToken.RevokedAt);
        Assert.Equal("hash-2", storedToken.ReplacedByTokenHash);
    }

    [Fact]
    public async Task UpdateAsync_does_nothing_for_an_unknown_token()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var unknownToken = CreateToken("unknown");
        unknownToken.RevokedAt = Now.AddMinutes(1);

        await CreateStore().UpdateAsync(unknownToken);

        Assert.Null(await Find("unknown"));
        Assert.Null((await Find("hash-1"))!.RevokedAt);
        Assert.Equal(1, await CountRecords());
    }

    [Fact]
    public async Task UpdateAsync_does_not_run_OnRecordCreated()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var callCount = 0;
        StoreOptions.OnRecordCreated = (_, _) => callCount++;

        await CreateStore().UpdateAsync(CreateToken("hash-1"));

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task TryRevokeActiveAsync_revokes_an_active_token_only_once()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var refreshTokenStore = CreateStore();

        Assert.True(await refreshTokenStore.TryRevokeActiveAsync("hash-1", Now.AddMinutes(1)));
        Assert.False(await refreshTokenStore.TryRevokeActiveAsync("hash-1", Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(1), (await Find("hash-1"))!.RevokedAt);
    }

    [Fact]
    public async Task TryRevokeActiveAsync_revokes_once_when_two_calls_run_at_the_same_time()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var firstStore = CreateStore();
        var secondStore = CreateStore();

        var results = await Task.WhenAll(
            Task.Run(() => firstStore.TryRevokeActiveAsync("hash-1", Now.AddMinutes(1))),
            Task.Run(() => secondStore.TryRevokeActiveAsync("hash-1", Now.AddMinutes(1))));

        Assert.Single(results, isRevoked => isRevoked);
    }

    [Fact]
    public async Task TryRevokeActiveAsync_does_not_revoke_a_token_at_its_expiry_time()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));

        var isRevoked = await CreateStore().TryRevokeActiveAsync("hash-1", Now.AddDays(30));

        Assert.False(isRevoked);
        Assert.Null((await Find("hash-1"))!.RevokedAt);
    }

    [Fact]
    public async Task TryRevokeActiveAsync_does_not_revoke_an_already_revoked_token()
    {
        var revokedToken = CreateToken("hash-1");
        revokedToken.RevokedAt = Now.AddMinutes(1);
        await CreateStore().AddAsync(revokedToken);

        var isRevoked = await CreateStore().TryRevokeActiveAsync("hash-1", Now.AddMinutes(2));

        Assert.False(isRevoked);
        Assert.Equal(Now.AddMinutes(1), (await Find("hash-1"))!.RevokedAt);
    }

    [Fact]
    public async Task TryRevokeActiveAsync_revokes_only_the_token_with_that_hash()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        await CreateStore().AddAsync(CreateToken("hash-2"));

        Assert.False(await CreateStore().TryRevokeActiveAsync("unknown", Now.AddMinutes(1)));
        Assert.True(await CreateStore().TryRevokeActiveAsync("hash-1", Now.AddMinutes(1)));

        Assert.Null((await Find("hash-2"))!.RevokedAt);
    }

    [Fact]
    public async Task CompleteRotationAsync_links_the_old_token_and_stores_the_replacement()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        await CreateStore().AddAsync(CreateToken("hash-other"));

        await CreateStore().CompleteRotationAsync("hash-1", CreateToken("hash-2"));

        Assert.Equal("hash-2", (await Find("hash-1"))!.ReplacedByTokenHash);
        Assert.NotNull(await Find("hash-2"));
        Assert.Null((await Find("hash-other"))!.ReplacedByTokenHash);
    }

    [Fact]
    public async Task CompleteRotationAsync_keeps_neither_change_when_saving_the_replacement_fails()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        await CreateStore().AddAsync(CreateToken("hash-taken"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => CreateStore().CompleteRotationAsync("hash-1", CreateToken("hash-taken")));

        Assert.Null((await Find("hash-1"))!.ReplacedByTokenHash);
        Assert.Equal(2, await CountRecords());
    }

    [Fact]
    public async Task CompleteRotationAsync_propagates_an_OnRecordCreated_failure_and_keeps_neither_change()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var failure = new InvalidOperationException("subject cannot be mapped");
        StoreOptions.OnRecordCreated = (_, _) => throw failure;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateStore().CompleteRotationAsync("hash-1", CreateToken("hash-2")));

        Assert.Same(failure, exception);
        Assert.Null((await Find("hash-1"))!.ReplacedByTokenHash);
        Assert.Null(await Find("hash-2"));
    }

    [Fact]
    public async Task RevokeAllForSubjectAsync_revokes_only_that_subjects_active_tokens()
    {
        var earlierRevokedToken = CreateToken("hash-3");
        earlierRevokedToken.RevokedAt = Now.AddMinutes(1);
        var otherSubjectToken = CreateToken("hash-4");
        otherSubjectToken.Subject = "user-2";
        await CreateStore().AddAsync(CreateToken("hash-1"));
        await CreateStore().AddAsync(CreateToken("hash-2"));
        await CreateStore().AddAsync(earlierRevokedToken);
        await CreateStore().AddAsync(otherSubjectToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        await CreateStore().RevokeAllForSubjectAsync("user-1");

        Assert.Equal(Now.AddMinutes(5), (await Find("hash-1"))!.RevokedAt);
        Assert.Equal(Now.AddMinutes(5), (await Find("hash-2"))!.RevokedAt);
        Assert.Equal(Now.AddMinutes(1), (await Find("hash-3"))!.RevokedAt);
        Assert.Null((await Find("hash-4"))!.RevokedAt);
    }

    [Fact]
    public async Task AddAsync_runs_OnRecordCreated_with_the_token_values_already_copied()
    {
        var token = CreateToken("hash-1");
        RefreshToken? receivedToken = null;
        string? receivedTokenHash = null;
        StoreOptions.OnRecordCreated = (refreshToken, record) =>
        {
            receivedToken = refreshToken;
            receivedTokenHash = record.TokenHash;
        };

        await CreateStore().AddAsync(token);

        Assert.Same(token, receivedToken);
        Assert.Equal("hash-1", receivedTokenHash);
    }

    [Fact]
    public async Task CompleteRotationAsync_runs_OnRecordCreated_for_the_replacement()
    {
        await CreateStore().AddAsync(CreateToken("hash-1"));
        var replacement = CreateToken("hash-2");
        RefreshToken? receivedToken = null;
        StoreOptions.OnRecordCreated = (refreshToken, _) => receivedToken = refreshToken;

        await CreateStore().CompleteRotationAsync("hash-1", replacement);

        Assert.Same(replacement, receivedToken);
    }

    [Fact]
    public async Task AddAsync_propagates_an_OnRecordCreated_failure_and_stores_nothing()
    {
        var failure = new InvalidOperationException("subject cannot be mapped");
        StoreOptions.OnRecordCreated = (_, _) => throw failure;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateStore().AddAsync(CreateToken("hash-1")));

        Assert.Same(failure, exception);
        Assert.Equal(0, await CountRecords());
    }

    protected EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, TRecord> CreateStore() =>
        Database.CreateStore(StoreOptions, _timeProvider);

    protected static RefreshToken CreateToken(string tokenHash) => new()
    {
        TokenHash = tokenHash,
        Subject = "user-1",
        CreatedAt = Now,
        ExpiresAt = Now.AddDays(30)
    };

    private Task<RefreshToken?> Find(string tokenHash) => CreateStore().FindByHashAsync(tokenHash);

    private Task<int> CountRecords() => Database.CreateContext().Set<TRecord>().CountAsync();
}
