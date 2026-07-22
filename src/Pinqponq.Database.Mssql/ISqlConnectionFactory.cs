using Microsoft.Data.SqlClient;

namespace Pinqponq.Database.Mssql;

/// <summary>
/// Opens SQL Server connections with transient-fault resiliency applied.
/// </summary>
public interface ISqlConnectionFactory
{
    /// <summary>Opens a new connection, retrying transient failures per configuration.</summary>
    Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
