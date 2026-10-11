using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Pinqponq.ErrorHandling.Postgres;

/// <summary>
/// Answers a <see cref="PostgresException"/>, thrown directly or wrapped in a
/// <see cref="DbUpdateException"/>, with 400 and a <c>database_*</c> code. The log event is
/// named after the <see cref="PostgresException"/> and carries its SQLSTATE, constraint,
/// table and column.
/// </summary>
public sealed class PostgresExceptionResolver : IExceptionResolver
{
    /// <inheritdoc />
    public ExceptionResolution? Resolve(Exception exception)
    {
        var postgresException = FindPostgresException(exception);
        if (postgresException is null)
        {
            return null;
        }

        var logDetail = new Dictionary<string, object?>
        {
            ["sqlState"] = postgresException.SqlState,
            ["constraintName"] = postgresException.ConstraintName,
            ["tableName"] = postgresException.TableName,
            ["columnName"] = postgresException.ColumnName,
        };

        return new ExceptionResolution(StatusCodes.Status400BadRequest, MapResponseCode(postgresException.SqlState))
        {
            FallbackResponseCode = PostgresErrorResponseCodes.DatabaseError,
            ReportedException = postgresException,
            LogDetail = logDetail,
        };
    }

    private static PostgresException? FindPostgresException(Exception exception) => exception switch
    {
        PostgresException postgresException => postgresException,
        DbUpdateException { InnerException: PostgresException wrappedPostgresException } => wrappedPostgresException,
        _ => null,
    };

    private static string MapResponseCode(string sqlState) => sqlState switch
    {
        PostgresErrorCodes.ForeignKeyViolation => PostgresErrorResponseCodes.DatabaseForeignKeyViolation,
        PostgresErrorCodes.UniqueViolation => PostgresErrorResponseCodes.DatabaseUniqueViolation,
        PostgresErrorCodes.NotNullViolation => PostgresErrorResponseCodes.DatabaseNotNullViolation,
        PostgresErrorCodes.StringDataRightTruncation => PostgresErrorResponseCodes.DatabaseValueTooLong,
        PostgresErrorCodes.InvalidTextRepresentation => PostgresErrorResponseCodes.DatabaseInvalidFormat,
        _ => PostgresErrorResponseCodes.DatabaseError,
    };
}
