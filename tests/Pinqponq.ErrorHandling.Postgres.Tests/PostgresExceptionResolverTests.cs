using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Pinqponq.ErrorHandling.Postgres.Tests;

public sealed class PostgresExceptionResolverTests
{
    private readonly PostgresExceptionResolver _resolver = new();

    internal static PostgresException CreatePostgresException(string sqlState) =>
        new(
            messageText: "violates constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            tableName: "orders",
            columnName: "customer_id",
            constraintName: "fk_orders_customer");

    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, PostgresErrorResponseCodes.DatabaseForeignKeyViolation)]
    [InlineData(PostgresErrorCodes.UniqueViolation, PostgresErrorResponseCodes.DatabaseUniqueViolation)]
    [InlineData(PostgresErrorCodes.NotNullViolation, PostgresErrorResponseCodes.DatabaseNotNullViolation)]
    [InlineData(PostgresErrorCodes.StringDataRightTruncation, PostgresErrorResponseCodes.DatabaseValueTooLong)]
    [InlineData(PostgresErrorCodes.InvalidTextRepresentation, PostgresErrorResponseCodes.DatabaseInvalidFormat)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, PostgresErrorResponseCodes.DatabaseError)]
    public void Postgres_exception_is_a_bad_request_with_the_code_of_its_sql_state(string sqlState, string expectedResponseCode)
    {
        var resolution = _resolver.Resolve(CreatePostgresException(sqlState));

        Assert.NotNull(resolution);
        Assert.Equal(StatusCodes.Status400BadRequest, resolution.StatusCode);
        Assert.Equal(expectedResponseCode, resolution.ResponseCode);
        Assert.Equal(PostgresErrorResponseCodes.DatabaseError, resolution.FallbackResponseCode);
    }

    [Fact]
    public void Resolution_reports_the_postgres_exception_with_its_constraint_detail()
    {
        var postgresException = CreatePostgresException(PostgresErrorCodes.ForeignKeyViolation);

        var resolution = _resolver.Resolve(postgresException);

        Assert.NotNull(resolution);
        Assert.Same(postgresException, resolution.ReportedException);
        Assert.NotNull(resolution.LogDetail);
        Assert.Equal(["sqlState", "constraintName", "tableName", "columnName"], resolution.LogDetail.Keys.ToArray());
        Assert.Equal("23503", resolution.LogDetail["sqlState"]);
        Assert.Equal("fk_orders_customer", resolution.LogDetail["constraintName"]);
        Assert.Equal("orders", resolution.LogDetail["tableName"]);
        Assert.Equal("customer_id", resolution.LogDetail["columnName"]);
    }

    [Fact]
    public void Postgres_exception_wrapped_by_ef_core_is_resolved_and_reported_unwrapped()
    {
        var postgresException = CreatePostgresException(PostgresErrorCodes.UniqueViolation);

        var resolution = _resolver.Resolve(new DbUpdateException("save failed", postgresException));

        Assert.NotNull(resolution);
        Assert.Equal(PostgresErrorResponseCodes.DatabaseUniqueViolation, resolution.ResponseCode);
        Assert.Same(postgresException, resolution.ReportedException);
    }

    [Fact]
    public void Ef_core_exception_without_a_postgres_cause_is_left_to_the_other_resolvers()
    {
        Assert.Null(_resolver.Resolve(new DbUpdateException("save failed", new TimeoutException())));
        Assert.Null(_resolver.Resolve(new DbUpdateException("save failed")));
    }

    [Fact]
    public void Other_exceptions_are_left_to_the_other_resolvers()
    {
        Assert.Null(_resolver.Resolve(new InvalidOperationException("boom", CreatePostgresException(PostgresErrorCodes.UniqueViolation))));
    }
}
