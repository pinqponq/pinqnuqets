using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Pinqponq.Database.Mongo.DependencyInjection;
using Pinqponq.Database.Mssql;
using Pinqponq.Database.Mssql.DependencyInjection;
using Pinqponq.Database.Postgres;
using Pinqponq.Database.Postgres.DependencyInjection;
using Pinqponq.Playground.Infrastructure;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Scenarios for the three database connection packages.</summary>
public static class DatabaseScenarios
{
    public static IEnumerable<Scenario> Create()
    {
        yield return PostgresQuery();
        yield return PostgresHealth();
        yield return PostgresRetry();
        yield return MongoRoundTrip();
        yield return MongoHealth();
        yield return MssqlQuery();
        yield return MssqlHealth();
    }

    private static Scenario PostgresQuery() => new(
        new ScenarioDescriptor
        {
            Id = "postgres.query",
            PackageId = "Pinqponq.Database.Postgres",
            Title = "Connect and run a query",
            Summary = "Opens a connection via IPostgresConnectionFactory, reads the server version, "
                      + "and writes to and reads from a temp table.",
            RequiredServices = [DevServiceIds.Postgres],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqPostgres(postgres =>
                postgres.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Postgres)));

            var factory = host.GetRequiredService<IPostgresConnectionFactory>();
            await using var connection = await factory.OpenConnectionAsync(context.CancellationToken);
            context.Step("Connection opened");

            await using (var version = connection.CreateCommand())
            {
                version.CommandText = "SELECT version()";
                var value = await version.ExecuteScalarAsync(context.CancellationToken);
                context.Artifact("server version", value?.ToString(), "text");
                context.Require("Version read", value is not null);
            }

            await using (var create = connection.CreateCommand())
            {
                create.CommandText =
                    "CREATE TEMP TABLE playground(id int primary key, name text); "
                    + "INSERT INTO playground VALUES (1, 'pinqponq');";
                await create.ExecuteNonQueryAsync(context.CancellationToken);
            }

            context.Step("Temp table created and populated");

            await using var select = connection.CreateCommand();
            select.CommandText = "SELECT name FROM playground WHERE id = 1";
            var name = await select.ExecuteScalarAsync(context.CancellationToken);

            context.Require("The written row was read back", name?.ToString() == "pinqponq", name?.ToString());
        });

    private static Scenario PostgresHealth() => new(
        new ScenarioDescriptor
        {
            Id = "postgres.health",
            PackageId = "Pinqponq.Database.Postgres",
            Title = "Postgres health-check",
            Summary = "Produces Healthy against the running container first, then Unhealthy "
                      + "against a closed port.",
            RequiredServices = [DevServiceIds.Postgres],
        },
        async context =>
        {
            await using var healthy = context.Host(services =>
            {
                services.AddPinqponqPostgres(postgres =>
                    postgres.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Postgres));
                services.AddHealthChecks().AddPinqponqPostgres();
            });

            var healthyReport = await healthy.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);
            context.Require("Running server is Healthy", healthyReport.Status == HealthStatus.Healthy,
                healthyReport.Status.ToString());
            context.Artifact("healthy report", HealthProjection.Project(healthyReport));

            await using var broken = context.Host(services =>
            {
                services.AddPinqponqPostgres(postgres =>
                {
                    postgres.ConnectionString =
                        "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Command Timeout=2";
                    postgres.RetryCount = 1; // Polly requires at least one attempt; 0 throws at pipeline build.
                });
                services.AddHealthChecks().AddPinqponqPostgres();
            });

            var brokenReport = await broken.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);
            context.Require("Closed port is Unhealthy", brokenReport.Status == HealthStatus.Unhealthy,
                brokenReport.Status.ToString());
            context.Artifact("unhealthy report", HealthProjection.Project(brokenReport));
        });

    private static Scenario PostgresRetry() => new(
        new ScenarioDescriptor
        {
            Id = "postgres.retry",
            PackageId = "Pinqponq.Database.Postgres",
            Title = "Retries on connection failure",
            Summary = "Attempts to connect to an unreachable server with different RetryCount "
                      + "values. Since Polly's OnRetry event isn't exposed outside the package, "
                      + "the proof is a time comparison: more attempts take noticeably longer. Also "
                      + "shows RetryCount=0 is rejected — Polly requires at least one attempt and "
                      + "throws while building the pipeline.",
            NegativePath = true,
            Fields =
            [
                new ScenarioField("retryCount", "RetryCount", ScenarioFieldKind.Number, "3"),
                new ScenarioField("retryBaseDelayMs", "RetryBaseDelay (ms)", ScenarioFieldKind.Duration, "200"),
            ],
            TimeoutSeconds = 90,
        },
        async context =>
        {
            const string Unreachable =
                "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Command Timeout=2";

            var retryCount = context.Input.Int("retryCount");
            var baseDelay = context.Input.Duration("retryBaseDelayMs");

            var zero = await MeasureFailureAsync(context, Unreachable, 0, baseDelay);
            context.Require(
                "RetryCount=0 is rejected",
                zero.ExceptionType?.Contains("Validation", StringComparison.OrdinalIgnoreCase) == true
                || zero.Exception?.Message.Contains("MaxRetryAttempts", StringComparison.Ordinal) == true,
                zero.Exception?.Message);

            var single = await MeasureFailureAsync(context, Unreachable, 1, baseDelay);
            context.Step($"RetryCount=1 → {single.ElapsedMs} ms", single.ExceptionType);
            context.Require("The single-attempt call failed", single.Exception is not null);

            var many = await MeasureFailureAsync(context, Unreachable, retryCount, baseDelay);
            context.Step($"RetryCount={retryCount} → {many.ElapsedMs} ms", many.ExceptionType);
            context.Require("The multi-attempt call also eventually failed", many.Exception is not null);

            context.Require(
                "More attempts added a measurable amount of extra time",
                many.ElapsedMs > single.ElapsedMs,
                $"{single.ElapsedMs} ms → {many.ElapsedMs} ms");

            context.Artifact("measurement", new
            {
                retry1Ms = single.ElapsedMs,
                retryNMs = many.ElapsedMs,
                retryCount,
                exceptionType = many.ExceptionType,
                retryCountZeroError = zero.Exception?.Message,
                note = "The attempt count can't be counted directly since Polly's OnRetry event isn't exposed outside the package.",
            });
        });

    private static async Task<FailureMeasurement> MeasureFailureAsync(
        ScenarioContext context,
        string connectionString,
        int retryCount,
        TimeSpan baseDelay)
    {
        await using var host = context.Host(services => services.AddPinqponqPostgres(postgres =>
        {
            postgres.ConnectionString = connectionString;
            postgres.RetryCount = retryCount;
            postgres.RetryBaseDelay = baseDelay;
        }));

        var stopwatch = Stopwatch.StartNew();
        Exception? thrown = null;

        try
        {
            // Resolution is inside the try on purpose: the factory builds its Polly pipeline
            // in its constructor, so an invalid RetryCount fails here rather than on use.
            var factory = host.GetRequiredService<IPostgresConnectionFactory>();
            await using var connection = await factory.OpenConnectionAsync(context.CancellationToken);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        stopwatch.Stop();
        return new FailureMeasurement(stopwatch.ElapsedMilliseconds, thrown, thrown?.GetType().Name);
    }

    private static Scenario MongoRoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "mongo.roundtrip",
            PackageId = "Pinqponq.Database.Mongo",
            Title = "Write, read, delete a document",
            Summary = "Writes a document to a collection via the registered IMongoDatabase, reads "
                      + "it back, and deletes it. Also verifies the database name comes from options.",
            RequiredServices = [DevServiceIds.Mongo],
            Fields = [new ScenarioField("databaseName", "DatabaseName", ScenarioFieldKind.Text, "playground")],
        },
        async context =>
        {
            var databaseName = context.Input.Text("databaseName");

            await using var host = context.Host(services => services.AddPinqponqMongo(mongo =>
            {
                mongo.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Mongo);
                mongo.DatabaseName = databaseName;
            }));

            var database = host.GetRequiredService<IMongoDatabase>();
            context.Require("Database name came from options",
                database.DatabaseNamespace.DatabaseName == databaseName,
                database.DatabaseNamespace.DatabaseName);

            var collection = database.GetCollection<BsonDocument>("scenarios");
            var id = ObjectId.GenerateNewId();
            var document = new BsonDocument
            {
                ["_id"] = id,
                ["name"] = "pinqponq",
                ["createdAt"] = DateTime.UtcNow,
            };

            await collection.InsertOneAsync(document, cancellationToken: context.CancellationToken);
            context.Step("Document written");

            var found = await collection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
                .FirstOrDefaultAsync(context.CancellationToken);

            context.Require("Document read back", found is not null);
            context.Require("Field preserved", found!["name"].AsString == "pinqponq");
            context.Artifact("document", found.ToJson(), "text");

            var deleted = await collection.DeleteOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", id),
                context.CancellationToken);
            context.Require("Document deleted", deleted.DeletedCount == 1);
        });

    private static Scenario MongoHealth() => new(
        new ScenarioDescriptor
        {
            Id = "mongo.health",
            PackageId = "Pinqponq.Database.Mongo",
            Title = "MongoDB health-check",
            Summary = "Healthy against a running server, Unhealthy against an unreachable address.",
            RequiredServices = [DevServiceIds.Mongo],
            TimeoutSeconds = 90,
        },
        async context =>
        {
            await using var healthy = context.Host(services =>
            {
                services.AddPinqponqMongo(mongo =>
                {
                    mongo.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.Mongo);
                    mongo.DatabaseName = "playground";
                });
                services.AddHealthChecks().AddPinqponqMongo();
            });

            var report = await healthy.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);
            context.Require("Running server is Healthy", report.Status == HealthStatus.Healthy, report.Status.ToString());
            context.Artifact("healthy report", HealthProjection.Project(report));

            await using var broken = context.Host(services =>
            {
                services.AddPinqponqMongo(mongo =>
                {
                    mongo.ConnectionString = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=1000&connectTimeoutMS=1000";
                    mongo.DatabaseName = "playground";
                });
                services.AddHealthChecks().AddPinqponqMongo();
            });

            var brokenReport = await broken.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);
            context.Require("Unreachable server is Unhealthy", brokenReport.Status == HealthStatus.Unhealthy,
                brokenReport.Status.ToString());
            context.Artifact("unhealthy report", HealthProjection.Project(brokenReport));
        });

    private static Scenario MssqlQuery() => new(
        new ScenarioDescriptor
        {
            Id = "mssql.query",
            PackageId = "Pinqponq.Database.Mssql",
            Title = "Connect and run a query",
            Summary = "Connects via ISqlConnectionFactory and reads @@VERSION. The SQL Server image "
                      + "is heavy (~1.5 GB) and doesn't run on ARM64.",
            RequiredServices = [DevServiceIds.MsSql],
            TimeoutSeconds = 90,
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqMssql(mssql =>
                mssql.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.MsSql)));

            await using var connection = await host.GetRequiredService<ISqlConnectionFactory>()
                .OpenConnectionAsync(context.CancellationToken);
            context.Step("Connection opened");

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT @@VERSION";
            var version = await command.ExecuteScalarAsync(context.CancellationToken);

            context.Require("Version read", version is not null);
            context.Artifact("server version", version?.ToString(), "text");
        });

    private static Scenario MssqlHealth() => new(
        new ScenarioDescriptor
        {
            Id = "mssql.health",
            PackageId = "Pinqponq.Database.Mssql",
            Title = "SQL Server health-check",
            Summary = "Healthy against a running server, Unhealthy against a closed port.",
            RequiredServices = [DevServiceIds.MsSql],
            TimeoutSeconds = 90,
        },
        async context =>
        {
            await using var healthy = context.Host(services =>
            {
                services.AddPinqponqMssql(mssql =>
                    mssql.ConnectionString = context.Stack.RequireConnectionString(DevServiceIds.MsSql));
                services.AddHealthChecks().AddPinqponqMssql();
            });

            var report = await healthy.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);
            context.Require("Running server is Healthy", report.Status == HealthStatus.Healthy, report.Status.ToString());
            context.Artifact("healthy report", HealthProjection.Project(report));

            await using var broken = context.Host(services =>
            {
                services.AddPinqponqMssql(mssql =>
                {
                    mssql.ConnectionString =
                        "Server=127.0.0.1,1;Database=master;User Id=sa;Password=none;TrustServerCertificate=true;Connect Timeout=2";
                    mssql.RetryCount = 1;
                });
                services.AddHealthChecks().AddPinqponqMssql();
            });

            var brokenReport = await broken.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(context.CancellationToken);
            context.Require("Closed port is Unhealthy", brokenReport.Status == HealthStatus.Unhealthy,
                brokenReport.Status.ToString());
            context.Artifact("unhealthy report", HealthProjection.Project(brokenReport));
        });

    private sealed record FailureMeasurement(long ElapsedMs, Exception? Exception, string? ExceptionType);
}

/// <summary>Projects a health report into something serialisable.</summary>
internal static class HealthProjection
{
    /// <summary>
    /// Flattens a report; <see cref="HealthReportEntry.Exception"/> must never be serialised
    /// directly.
    /// </summary>
    public static object Project(HealthReport report) => new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        entries = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => (object)new
            {
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
                exceptionType = entry.Value.Exception?.GetType().FullName,
                exceptionMessage = entry.Value.Exception?.Message,
            },
            StringComparer.Ordinal),
    };
}
