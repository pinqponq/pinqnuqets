using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Pinqponq.ErrorHandling.Postgres;

/// <summary>
/// Answers a <see cref="PostgresException"/>, thrown directly or wrapped in a
/// <see cref="DbUpdateException"/>: a mapped SQLSTATE with 400 and its <c>database_*</c> code, any
/// other with <see cref="PostgresErrorHandlingOptions.UnmappedStatusCode"/> and <c>database_error</c>. The log event is
/// named after the <see cref="PostgresException"/> and carries its SQLSTATE, constraint,
/// table and column.
/// </summary>
public sealed class PostgresExceptionResolver : IExceptionResolver
{
    private readonly int _unmappedStatusCode;

    /// <summary>Creates the resolver.</summary>
    public PostgresExceptionResolver(IOptions<PostgresErrorHandlingOptions> postgresErrorHandlingOptions)
    {
        _unmappedStatusCode = postgresErrorHandlingOptions.Value.UnmappedStatusCode;
    }

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

        var responseCode = MapResponseCode(postgresException.SqlState);
        var isMappedSqlState = responseCode != PostgresErrorResponseCodes.DatabaseError;
        var statusCode = isMappedSqlState ? StatusCodes.Status400BadRequest : _unmappedStatusCode;
        return new ExceptionResolution(statusCode, responseCode)
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
