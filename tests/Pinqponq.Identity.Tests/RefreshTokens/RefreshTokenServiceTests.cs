using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.Tests.RefreshTokens;

public sealed class RefreshTokenServiceTests : RefreshTokenServiceContractTests
{
    private readonly InMemoryRefreshTokenStore _store = new();

    protected override IRefreshTokenStore CreateStore() => _store;

    protected override Task<int> CountStoredTokens() => Task.FromResult(_store.Count);

    protected override Task<int> CountActiveTokens() => Task.FromResult(_store.ActiveCount);

    protected override void FailNextRotation(Exception failure)
    {
        _store.FailAfterAddBeforeLink = true;
        _store.ThrowOnCompleteRotation = failure;
    }
}
