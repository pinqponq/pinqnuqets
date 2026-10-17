namespace Pinqponq.ErrorHandling.Postgres;

/// <summary>
/// Response codes produced for PostgreSQL errors; their messages ship in this package's catalog.
/// </summary>
public static class PostgresErrorResponseCodes
{
    /// <summary>A PostgreSQL error without a more specific code.</summary>
    public const string DatabaseError = "database_error";

    /// <summary>SQLSTATE 23503.</summary>
    public const string DatabaseForeignKeyViolation = "database_foreign_key_violation";

    /// <summary>SQLSTATE 23505.</summary>
    public const string DatabaseUniqueViolation = "database_unique_violation";

    /// <summary>SQLSTATE 23502.</summary>
    public const string DatabaseNotNullViolation = "database_not_null_violation";

    /// <summary>SQLSTATE 22001.</summary>
    public const string DatabaseValueTooLong = "database_value_too_long";

    /// <summary>SQLSTATE 22P02.</summary>
    public const string DatabaseInvalidFormat = "database_invalid_format";
}
