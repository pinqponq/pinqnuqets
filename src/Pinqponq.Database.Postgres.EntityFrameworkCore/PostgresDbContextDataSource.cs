using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Pinqponq.Database.Postgres.EntityFrameworkCore;

/// <summary>
/// The <see cref="NpgsqlDataSource"/> (connection pool) shared by every
/// <typeparamref name="TDbContext"/> instance. A singleton, disposed with the container.
/// </summary>
/// <typeparam name="TDbContext">The context the data source belongs to.</typeparam>
public sealed class PostgresDbContextDataSource<TDbContext> : IDisposable
    where TDbContext : DbContext
{
    internal PostgresDbContextDataSource(NpgsqlDataSource dataSource)
    {
        DataSource = dataSource;
    }

    /// <summary>The data source handed to <c>UseNpgsql</c>.</summary>
    public NpgsqlDataSource DataSource { get; }

    /// <inheritdoc />
    public void Dispose() => DataSource.Dispose();
}
