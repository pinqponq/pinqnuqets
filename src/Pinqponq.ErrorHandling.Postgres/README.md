# Pinqponq.ErrorHandling.Postgres

PostgreSQL error mapping for [Pinqponq.ErrorHandling](../Pinqponq.ErrorHandling/README.md).
A `PostgresException` from Npgsql — thrown directly or wrapped in an EF Core
`DbUpdateException` — is answered with a stable `database_*` code instead of a driver message:
constraint and value errors with HTTP 400, other database errors with 500 (configurable).

## Install

```bash
dotnet add package Pinqponq.ErrorHandling.Postgres
```

## Quick start

```csharp
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.ErrorHandling.Postgres.DependencyInjection;

builder.Services.AddPinqponqErrorHandling();
builder.Services.AddPinqponqPostgresErrorHandling();

app.UsePinqponqErrorHandling();
```

## Mapping

| SQLSTATE | `Npgsql.PostgresErrorCodes` | Response code |
|---|---|---|
| 23503 | `ForeignKeyViolation` | `database_foreign_key_violation` |
| 23505 | `UniqueViolation` | `database_unique_violation` |
| 23502 | `NotNullViolation` | `database_not_null_violation` |
| 22001 | `StringDataRightTruncation` | `database_value_too_long` |
| 22P02 | `InvalidTextRepresentation` | `database_invalid_format` |
| anything else | | `database_error` |

The mapped SQLSTATEs are answered 400; anything else (connection loss, deadlock, missing table, …)
with 500.
The constants are in `PostgresErrorResponseCodes`.

## Logging

The request error detail (`Pinqponq.Contracts.IRequestErrorFeature`) is named after the
`PostgresException` (`exception_PostgresException`), also when EF Core wrapped it, and
carries `sqlState`, `constraintName`, `tableName` and `columnName`.

## Localized messages

`AddPinqponqPostgresErrorHandling` adds this package's catalog (the six `database_*` codes in
12 languages: AR, DE, EN, ES, FR, HI, ID, IT, JA, KO, PT, TR) to the error localization of
Pinqponq.ErrorHandling. With `AddPinqponqErrorLocalization` on, a code the catalog does not
know falls back to `database_error`.

## Dependencies

`Npgsql` (8.0.5 or later) and `Microsoft.EntityFrameworkCore` (8.0.31 or later, for
`DbUpdateException`). An application on newer versions resolves to its own.

## Repository

https://github.com/pinqponq/pinqnuqets
