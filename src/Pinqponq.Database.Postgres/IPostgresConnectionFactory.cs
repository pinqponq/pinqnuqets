using Npgsql;

namespace Pinqponq.Database.Postgres;

/// <summary>
/// Opens Postgres connections with transient-fault resiliency applied.
/// </summary>
public interface IPostgresConnectionFactory
{
    /// <summary>Opens a new connection, retrying transient failures per configuration.</summary>
    Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
