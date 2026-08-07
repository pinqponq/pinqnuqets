# Pinqponq.Playground — package and log test console

An ASP.NET Core application that exercises the repo's 13 `Pinqponq.*` packages from a
browser, against **real** dependencies. Every run shows two things at once: what the
package does, and **which structured log records it produces** while doing it.

```bash
dotnet run --project samples/Pinqponq.Playground
# → http://127.0.0.1:5199
```

Packages are referenced as **source** (`ProjectReference`), so a change under `src/`
shows up in the console instantly.

## Is Docker required?

No — no container is started on launch, the app comes up instantly.
Most scenarios work without Docker: Identity, OTP (SMS), TOTP, SSO negative paths,
ErrorHandling, and all of SMS (SMS traffic goes to the console's own fake NetGSM
endpoint — HTTPS ApiUrl + loopback rewrite; GET and RestV2).

If Docker is available, click the service in the top strip and say **Start**;
Testcontainers spins up a container and the scenarios tied to that service unlock.
Shutting a service down with **Stop** is used to demonstrate the health-check
dropping to `Unhealthy` and the retry behavior.

| Service | Image | Scenarios it unlocks |
|---|---|---|
| PostgreSQL | `postgres:16-alpine` | Database.Postgres |
| Redis | `redis:7.4-alpine` | Cache |
| RabbitMQ | `rabbitmq:3.13-alpine` | Messaging.RabbitMq |
| MongoDB | `mongo:7.0` | Database.Mongo |
| MailHog | `mailhog/mailhog:v1.0.1` | Mail, Otp's email channel |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` | Database.Mssql (heavy, ~1.5 GB, no ARM64) |

Image tags are identical to `tests/Pinqponq.TestSupport/Fixtures/`, so the console
and the integration tests run against the same server versions.

### Alternatives

- **Fixed-port environment**: `docker compose -f samples/Pinqponq.Playground/docker-compose.yml up -d`
- **Existing servers**: write a connection string under `appsettings.json` →
  `Playground:ExternalServices`; that service is marked "External" and no container
  is started. Mixed usage (external Redis + containerized Postgres) is supported.

## How it works

**Each scenario runs in its own DI container.** The scenario body calls the package's
own `AddPinqponqXxx(...)` extension — so the registration code itself is exercised too.
The container is `DisposeAsync`d when the run finishes, so the `IOptions<T>` cache
doesn't leak between runs and every options field you change from the UI actually
takes effect. Singleton resources such as `NpgsqlDataSource` / `IConnectionMultiplexer`
don't leak either, for the same reason.

**Logs are tied to the run.** The run's log provider is set up with a `runId`, so the
record is structurally stamped — this works correctly even where the ambient context
doesn't flow, such as a RabbitMQ consumer running in the background. The **"N logs →"**
button on the result card filters the bottom console to that run. Expanding a line
shows the `messageTemplate` and the structured fields in their raw form — the only way
to verify the `TraceId` / `CorrelationId` / `ResponseCode` field names produced by
`Pinqponq.ErrorHandling`.

## Highlighted scenarios

| Scenario | What it proves |
|---|---|
| `errorhandling.log-shape` | The middleware's log record: message template, field names, level (500 → Error, 4xx → Warning). |
| `errorhandling.correlation` | Incoming `X-Correlation-ID` → the response's `traceId` **and** the log's `CorrelationId`; the log's `TraceId` is the request's own identity, the two differ. |
| `sms.retry` | The fake endpoint returns 500 for the first N requests; the attempts and the exponential delay between them are shown as a table. |
| `otp.sms` / `otp.email` | Since the code only leaves through the channel, it's read back and verified from the fake SMS record or the MailHog inbox. |
| `identity.refresh.rotate` | Only a SHA-256 hash is kept in the store; the old record is revoked and chained to the new one. |
| `cache.lock` | Locking the same resource from two separate containers: the second gets `Acquired=false`, and once released the third attempt succeeds. |
| `rabbit.dead-letter` | When the handler throws, the message lands in `{queue}.dead` **and** the `Queue` field of the error log the package produces. |

## Keyboard

| Shortcut | Action |
|---|---|
| <kbd>Ctrl/⌘ K</kbd> | Search scenarios |
| <kbd>Ctrl/⌘ ↵</kbd> | Run the open scenario |
| <kbd>/</kbd> | Focus the sidebar filter |

## HTTP API

The UI runs entirely on these endpoints; you can also drive it with curl.

| Endpoint | Action |
|---|---|
| `GET /api/catalog` | Packages, scenarios, form fields, why a given scenario is disabled |
| `POST /api/scenarios/{id}/run` | `{"input":{...}}` → steps, outputs, and the run's logs |
| `GET /api/infra`, `POST /api/infra/{id}/start\|stop\|restart` | Service status and control |
| `GET /api/logs`, `GET /api/logs/stream` | Log history and live stream (SSE) |
| `GET /api/mail`, `GET /api/sms` | MailHog inbox, requests received by the fake NetGSM |
| `GET /sandbox/errors/{case}` | Real pipeline with `UsePinqponqErrorHandling()` applied |

```bash
curl -i -H 'X-Correlation-ID: pinq-123' http://127.0.0.1:5199/sandbox/errors/unauthorized
curl -s 'http://127.0.0.1:5199/api/logs?q=pinq-123' | jq '.entries[0].state'
```

## Notes

- The app only listens on `127.0.0.1`: it's a tool where real credentials can be
  entered and it opens outbound connections.
- `dotnet watch` recreates containers on every rebuild; prefer `dotnet run`.
- The app removes the containers it started when it shuts down. Testcontainers'
  resource reaper handles leftovers after an unexpected termination.
