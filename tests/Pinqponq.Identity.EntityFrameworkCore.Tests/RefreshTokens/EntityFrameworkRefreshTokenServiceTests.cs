using Microsoft.EntityFrameworkCore;
using Pinqponq.Identity.RefreshTokens;
using Pinqponq.Identity.Tests.RefreshTokens;
using Xunit;

namespace Pinqponq.Identity.EntityFrameworkCore.Tests.RefreshTokens;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EntityFrameworkRefreshTokenServiceTests(PostgresCollectionFixture fixture)
    : RefreshTokenServiceContractTests, IAsyncLifetime
{
    private readonly EntityFrameworkRefreshTokenStoreOptions<OwnedRefreshTokenRecord> _storeOptions = new();
    private TestDatabase _database = null!;
    private Exception? _nextRotationFailure;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(fixture);
        _storeOptions.OnRecordCreated = (_, _) => ThrowNextRotationFailure();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    protected override IRefreshTokenStore CreateStore() => _database.CreateStore(_storeOptions, TimeProvider.System);

    protected override Task<int> CountStoredTokens() =>
        _database.CreateContext().Set<OwnedRefreshTokenRecord>().CountAsync();

    protected override Task<int> CountActiveTokens()
    {
        var now = DateTime.UtcNow;
        return _database.CreateContext()
            .Set<OwnedRefreshTokenRecord>()
            .CountAsync(record => record.RevokedAt == null && record.ExpiresAt > now);
    }

    protected override void ThrowOnNextCompleteRotation(Exception failure) => _nextRotationFailure = failure;

    private void ThrowNextRotationFailure()
    {
        if (_nextRotationFailure is not { } failure)
        {
            return;
        }

        _nextRotationFailure = null;
        throw failure;
    }
}
