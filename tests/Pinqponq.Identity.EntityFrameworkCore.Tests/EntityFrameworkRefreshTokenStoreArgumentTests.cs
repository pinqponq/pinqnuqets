using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.EntityFrameworkCore.DependencyInjection;
using Xunit;

namespace Pinqponq.Identity.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkRefreshTokenStoreArgumentTests : IDisposable
{
    private const string UnusedConnectionString = "Host=refresh-tokens-db;Database=refresh_tokens;Username=app;Password=app";

    private readonly TestRefreshTokenDbContext _dbContext = new(
        new DbContextOptionsBuilder<TestRefreshTokenDbContext>().UseNpgsql(UnusedConnectionString).Options);

    private readonly IOptions<EntityFrameworkRefreshTokenStoreOptions<OwnedRefreshTokenRecord>> _storeOptions =
        Options.Create(new EntityFrameworkRefreshTokenStoreOptions<OwnedRefreshTokenRecord>());

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public void Constructor_rejects_a_missing_context()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, OwnedRefreshTokenRecord>(null!, _storeOptions, TimeProvider.System));

        Assert.Equal("dbContext", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_missing_options()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, OwnedRefreshTokenRecord>(_dbContext, null!, TimeProvider.System));

        Assert.Equal("storeOptions", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_missing_clock()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, OwnedRefreshTokenRecord>(_dbContext, _storeOptions, null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public async Task AddAsync_rejects_a_missing_token()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => CreateStore().AddAsync(null!));

        Assert.Equal("token", exception.ParamName);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_missing_token()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => CreateStore().UpdateAsync(null!));

        Assert.Equal("token", exception.ParamName);
    }

    [Fact]
    public async Task CompleteRotationAsync_rejects_a_missing_replacement()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => CreateStore().CompleteRotationAsync("hash-1", null!));

        Assert.Equal("replacement", exception.ParamName);
    }

    [Fact]
    public void Registration_rejects_a_missing_service_collection()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddPinqponqEntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, OwnedRefreshTokenRecord>());

        Assert.Equal("services", exception.ParamName);
    }

    private EntityFrameworkRefreshTokenStore<TestRefreshTokenDbContext, OwnedRefreshTokenRecord> CreateStore() =>
        new(_dbContext, _storeOptions, TimeProvider.System);
}
