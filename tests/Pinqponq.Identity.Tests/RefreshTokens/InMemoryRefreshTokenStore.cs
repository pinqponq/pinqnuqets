using System.Collections.Concurrent;
using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.Tests.RefreshTokens;

/// <summary>
/// Minimal in-memory <see cref="IRefreshTokenStore"/> for tests.
/// </summary>
internal sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshToken> _byHash = new();

    public int Count => _byHash.Count;

    public Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _byHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        _byHash.TryGetValue(tokenHash, out var token);
        return Task.FromResult(token);
    }

    public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _byHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }
}
