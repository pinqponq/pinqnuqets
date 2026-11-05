using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pinqponq.Identity.RefreshTokens;

namespace Pinqponq.Identity.EntityFrameworkCore;

public sealed class EntityFrameworkRefreshTokenStore<TDbContext, TRecord> : IRefreshTokenStore
    where TDbContext : DbContext
    where TRecord : class, IRefreshTokenRecord, new()
{
    private readonly TDbContext _dbContext;
    private readonly EntityFrameworkRefreshTokenStoreOptions<TRecord> _storeOptions;
    private readonly TimeProvider _timeProvider;

    public EntityFrameworkRefreshTokenStore(
        TDbContext dbContext,
        IOptions<EntityFrameworkRefreshTokenStoreOptions<TRecord>> storeOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbContext = dbContext;
        _storeOptions = storeOptions.Value;
        _timeProvider = timeProvider;
    }

    private DbSet<TRecord> Records => _dbContext.Set<TRecord>();

    /// <inheritdoc />
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        Records.Add(CreateRecord(token));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var record = await Records
            .AsNoTracking()
            .FirstOrDefaultAsync(refreshToken => refreshToken.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : ToToken(record);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var record = await Records
            .FirstOrDefaultAsync(refreshToken => refreshToken.TokenHash == token.TokenHash, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        CopyTokenValues(token, record);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryRevokeActiveAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        var revokedAtUtc = revokedAt.UtcDateTime;
        var affectedRowCount = await Records
            .Where(refreshToken =>
                refreshToken.TokenHash == tokenHash
                && refreshToken.RevokedAt == null
                && refreshToken.ExpiresAt > revokedAtUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(refreshToken => refreshToken.RevokedAt, revokedAtUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return affectedRowCount > 0;
    }

    /// <inheritdoc />
    public async Task CompleteRotationAsync(
        string revokedTokenHash,
        RefreshToken replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await Records
                .Where(refreshToken => refreshToken.TokenHash == revokedTokenHash)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(refreshToken => refreshToken.ReplacedByTokenHash, replacement.TokenHash),
                    cancellationToken)
                .ConfigureAwait(false);

            Records.Add(CreateRecord(replacement));
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default)
    {
        var revokedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return Records
            .Where(refreshToken => refreshToken.Subject == subject && refreshToken.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(refreshToken => refreshToken.RevokedAt, revokedAtUtc),
                cancellationToken);
    }

    private TRecord CreateRecord(RefreshToken token)
    {
        var record = new TRecord
        {
            CreatedAt = token.CreatedAt.UtcDateTime
        };
        CopyTokenValues(token, record);
        _storeOptions.OnRecordCreated(token, record);
        return record;
    }

    private static void CopyTokenValues(RefreshToken token, TRecord record)
    {
        record.Subject = token.Subject;
        record.TokenHash = token.TokenHash;
        record.ExpiresAt = token.ExpiresAt.UtcDateTime;
        record.RevokedAt = token.RevokedAt?.UtcDateTime;
        record.ReplacedByTokenHash = token.ReplacedByTokenHash;
    }

    private static RefreshToken ToToken(TRecord record) => new()
    {
        Subject = record.Subject,
        TokenHash = record.TokenHash,
        CreatedAt = AsUtc(record.CreatedAt),
        ExpiresAt = AsUtc(record.ExpiresAt),
        RevokedAt = record.RevokedAt is { } revokedAt ? AsUtc(revokedAt) : null,
        ReplacedByTokenHash = record.ReplacedByTokenHash
    };

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
