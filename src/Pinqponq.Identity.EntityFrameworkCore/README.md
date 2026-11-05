# Pinqponq.Identity.EntityFrameworkCore

An EF Core implementation of the `IRefreshTokenStore` that
[Pinqponq.Identity](../Pinqponq.Identity/README.md) needs for refresh tokens. The store works on
the application's own `DbContext` and its own refresh token entity. The package ships no entity,
no table name, no column names and no migration.

`Pinqponq.Identity` itself has no storage dependency. Applications that do not use EF Core write
their own `IRefreshTokenStore` instead.

## Install

```bash
dotnet add package Pinqponq.Identity.EntityFrameworkCore
```

## Quick start

1. Implement `IRefreshTokenRecord` on the application's entity. The entity keeps its own key,
   table, columns and foreign keys.

```csharp
public class UserRefreshToken : IRefreshTokenRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string Subject { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
```

2. Register the store next to `AddPinqponqIdentity`. The application registers its own
   `DbContext`.

```csharp
using Pinqponq.Identity.EntityFrameworkCore.DependencyInjection;

builder.Services.AddPinqponqIdentity(jwt => { /* ... */ });
builder.Services.AddPinqponqEntityFrameworkRefreshTokenStore<AppDbContext, UserRefreshToken>(storeOptions =>
{
    // The application's own columns that come from the token. When this is not set, nothing
    // extra is written.
    storeOptions.OnRecordCreated = (refreshToken, record) =>
        record.UserId = int.Parse(refreshToken.Subject, NumberStyles.None, CultureInfo.InvariantCulture);
});
```

## What the store does

| Method | Query |
|---|---|
| `AddAsync` | Adds one record and saves it. |
| `FindByHashAsync` | Reads the record with that `TokenHash` without tracking it. Returns `null` when there is none. |
| `UpdateAsync` | Copies the token's values onto the stored record and saves it. Does nothing when no record has that `TokenHash`. |
| `TryRevokeActiveAsync` | One `UPDATE` that sets `RevokedAt` only where `RevokedAt` is empty and `ExpiresAt` is later than the revoke time. The database decides, so two calls at the same time revoke the token once: one returns `true`, the other `false`. |
| `CompleteRotationAsync` | In one transaction: sets `ReplacedByTokenHash` on the revoked record and adds the replacement. When either step fails, neither is kept. |
| `RevokeAllForSubjectAsync` | One `UPDATE` that sets `RevokedAt` on every record of that subject whose `RevokedAt` is empty. |

- Every `DateTime` is written as UTC. Values read back are returned as UTC as well.
- `RevokeAllForSubjectAsync` takes the time from `TimeProvider`. The registration adds
  `TimeProvider.System` when the application has not registered a `TimeProvider`.
- The store is registered as a scoped `IRefreshTokenStore`, so it shares the request's `DbContext`.
  Its `SaveChangesAsync` calls (in `AddAsync`, `UpdateAsync` and `CompleteRotationAsync`) therefore
  also save any other changes pending in that context, inside the rotation transaction for
  `CompleteRotationAsync`.
- `CompleteRotationAsync` starts its own transaction. It cannot run inside a transaction that the
  application already opened on the same context. A context that uses a retrying execution
  strategy (`EnableRetryOnFailure`) also rejects it, because EF Core does not retry a transaction
  that the caller started.

## Options

| Option | Default | Notes |
|---|---|---|
| `OnRecordCreated` | Does nothing | Runs for every new record, from `AddAsync` and `CompleteRotationAsync`, after the token's values are copied and before the save. An exception thrown here stops the save and propagates to the caller, so an application can reject a subject it cannot map. It does not run for `UpdateAsync`. |

## What stays in the application

- The entity, its table, its column configuration (`IEntityTypeConfiguration`) and its migrations.
- The foreign key and how `Subject` maps to it (`OnRecordCreated`).
- A unique index on `TokenHash`, because every query of the store except `RevokeAllForSubjectAsync`
  filters on it, and an index on `Subject` for `RevokeAllForSubjectAsync`. The package cannot add
  them, because it has no part in the application's model.

Because the package adds no table and no column, moving an existing store onto it needs no
migration. When the entity's property names differ from `IRefreshTokenRecord` (for example
`ExpiresAtUtc`), rename the properties and keep the old column names with `HasColumnName`:

```csharp
entity.Property(record => record.ExpiresAt).HasColumnName("ExpiresAtUtc");
```

## Dependencies

`Pinqponq.Identity` and `Microsoft.EntityFrameworkCore.Relational` 8.0.31 or later. An application
on EF Core 9 or 10 resolves to its own version.

## Repository

https://github.com/pinqponq/pinqnuqets
