using Microsoft.AspNetCore.Http;

namespace Pinqponq.ErrorHandling.Postgres;

/// <summary>
/// Configuration for <see cref="PostgresExceptionResolver"/>.
/// </summary>
public sealed class PostgresErrorHandlingOptions
{
    /// <summary>
    /// Status of a PostgreSQL error without a mapped SQLSTATE (connection loss, deadlock, missing
    /// table, …), answered with <c>database_error</c>. Defaults to 500. The mapped constraint and
    /// value errors are always 400.
    /// </summary>
    public int UnmappedStatusCode { get; set; } = StatusCodes.Status500InternalServerError;
}
