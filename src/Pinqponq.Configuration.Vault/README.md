# Pinqponq.Configuration.Vault

A [HashiCorp Vault](https://developer.hashicorp.com/vault) configuration provider for
.NET. At startup it reads **one KV version 2 record shaped like `appsettings.json`**
and lays it over the configuration your application already has, so secrets leave
your settings files while every `IOptions<T>` and `GetSection(...)` call keeps working
unchanged. Requests are plain `HttpClient` calls — no Vault client library is pulled in.

It finds the token the way the Vault CLI does, so a developer who ran `vault login`
once needs no per-project setup.

## Install

```bash
dotnet add package Pinqponq.Configuration.Vault
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- A Vault server with a **KV version 2** secrets engine
- For local development: the [Vault CLI](#vault-cli-setup-for-local-development)

## Quick start

Describe where the record lives in `appsettings.json`. None of these values is a secret:

```json
{
  "Vault": {
    "Address": "https://vault.example.com:8200",
    "Mount": "apps",
    "Path": "my-product/my-service"
  }
}
```

Add the source first thing in `Program.cs`, before any setting is read:

```csharp
using Pinqponq.Configuration.Vault;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddPinqponqVault();
```

It works the same with `Host.CreateApplicationBuilder(args)` for worker services.

Store the record in Vault with the same nesting your settings file uses:

```json
{
  "ConnectionStrings": {
    "PostgreSql": "Host=db;Database=app;Username=app;Password=..."
  },
  "RabbitMQ": {
    "Password": "..."
  }
}
```

Each nested key becomes a configuration key (`ConnectionStrings:PostgreSql`,
`RabbitMQ:Password`). Keys that the record does not contain keep the value from your
settings files, so the record only needs the secrets.

## Configuration

`AddPinqponqVault(sectionName = "Vault")` reads these keys from the section:

| Key | Required | Notes |
|---|---|---|
| `Address` | yes | Vault server address. Falls back to the `VAULT_ADDR` environment variable. |
| `Mount` | yes | KV version 2 secrets engine mount. |
| `Path` | yes | Path of the record under the mount. |
| `Token` | no | See [How the token is found](#how-the-token-is-found). Keep it out of source control. |
| `Namespace` | no | Vault Enterprise namespace. |
| `RequestTimeoutSeconds` | no | Timeout of one request to Vault. Default `10`. |

To supply the settings in code instead, use the other overload:

```csharp
builder.Configuration.AddPinqponqVault(vaultOptions =>
{
    vaultOptions.Address = "https://vault.example.com:8200";
    vaultOptions.Mount = "apps";
    vaultOptions.Path = "my-product/my-service";
});
```

`VaultConfigurationOptions` also exposes `MaxAttemptCount` (default `3`),
`RetryDelay` (default 2 seconds) and `HttpMessageHandler`, e.g. to trust a private
certificate authority.

## How the token is found

The first of these that has a value is used:

1. The `Token` setting — in practice the `Vault__Token` environment variable or user secrets.
2. The `VAULT_TOKEN` environment variable.
3. The token file the Vault CLI writes on `vault login` (`~/.vault-token`).

This gives each environment a natural fit:

| Where | Identity | How the token arrives |
|---|---|---|
| Servers and containers | A token for the service, with a read-only policy on its own record | `VAULT_TOKEN` in the container environment |
| A developer's machine | The developer's own Vault account | `vault login`, once |

## Vault CLI setup for local development

Install the Vault CLI:

```bash
# Windows
winget install HashiCorp.Vault

# macOS
brew tap hashicorp/tap
brew install hashicorp/tap/vault
```

For Linux and other options see the
[official install guide](https://developer.hashicorp.com/vault/install).

Log in with your own account. The address is the same one your `Vault:Address` setting holds:

```bash
vault login -address=https://vault.example.com:8200 -method=userpass username=<your-user-name>
```

The CLI saves the token to `~/.vault-token`. From then on every project that uses this
package starts without further setup. When Vault rejects the saved token — the login
expired, or it was made against a different Vault server than the one the application
points at — startup fails with a message that names the server and the `vault login`
command to run.

## Main types

| Type | Role |
|---|---|
| `VaultConfigurationExtensions` | `AddPinqponqVault(...)` on `IConfigurationManager` and `IConfigurationBuilder`. |
| `VaultConfigurationOptions` | Where the record lives and how to reach Vault. |
| `VaultConfigurationSource` / `VaultConfigurationProvider` | The configuration source and provider, for manual registration. |
| `VaultConfigurationException` | Thrown when the record cannot be loaded. |

## Notes / behavior

- **Startup fails fast.** A missing setting, a missing record, a rejected token or an
  unreachable Vault throws `VaultConfigurationException` while the host is being built.
  An unreachable Vault or a server error is retried `MaxAttemptCount` times first; a
  missing record or a rejected token is not.
- **Vault wins.** The record is added after the sources registered before the call, so
  its values override settings files and environment variables.
- **Read once.** The record is loaded at startup and is not reloaded. Restart the
  application to pick up a new version of the record.
- **No secret in messages.** Exception messages name the record and the setting, never
  a value or the token.
- **KV version 2 only.** A KV version 1 mount is reported as a malformed record.
- **Non-string values.** Numbers and booleans are exposed in their JSON text form
  (`5672`, `true`); arrays are exposed by index (`Hosts:0`, `Hosts:1`).

## Repository

https://github.com/pinqponq/pinqnuqets
