using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.EntityFrameworkCore;

/// <summary>
/// Options for <see cref="EntityFrameworkRefreshTokenStore{TDbContext, TRecord}"/>.
/// </summary>
/// <typeparam name="TRecord">The application's refresh token entity.</typeparam>
public sealed class EntityFrameworkRefreshTokenStoreOptions<TRecord>
    where TRecord : class, IRefreshTokenRecord, new()
{
    /// <summary>
    /// Runs for every new record, after the token's values are copied into it and before it is
    /// saved. Use it to set the application's own columns from the token, for example a user
    /// foreign key parsed from <see cref="RefreshToken.Subject"/>. An exception thrown here stops
    /// the save and propagates from <see cref="IRefreshTokenStore.AddAsync"/> and
    /// <see cref="IRefreshTokenStore.CompleteRotationAsync"/>. The default does nothing.
    /// </summary>
    public Action<RefreshToken, TRecord> OnRecordCreated { get; set; } = static (_, _) => { };
}
