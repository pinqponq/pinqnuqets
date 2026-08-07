namespace Pinqponq.Playground.Scenarios;

/// <summary>One package as presented in the console's navigation.</summary>
public sealed record PackageDescriptor(string Id, string Title, string Group, string Summary);

/// <summary>
/// The explicit list of everything the console can run.
/// </summary>
/// <remarks>
/// Registration is hand-written rather than reflection-scanned: the order is meaningful in
/// the UI, a renamed scenario breaks the build instead of silently disappearing, and the
/// list doubles as a readable coverage document for the 13 packages.
/// </remarks>
public sealed class ScenarioCatalog
{
    private readonly Dictionary<string, Scenario> _byId;

    public ScenarioCatalog()
    {
        var scenarios = new List<Scenario>();
        scenarios.AddRange(IdentityScenarios.Create());
        scenarios.AddRange(OtpScenarios.Create());
        scenarios.AddRange(TotpScenarios.Create());
        scenarios.AddRange(SsoScenarios.Create());
        scenarios.AddRange(CacheScenarios.Create());
        scenarios.AddRange(SmsScenarios.Create());
        scenarios.AddRange(MailScenarios.Create());
        scenarios.AddRange(DatabaseScenarios.Create());
        scenarios.AddRange(RabbitMqScenarios.Create());
        scenarios.AddRange(ErrorHandlingScenarios.Create());

        All = scenarios;
        _byId = scenarios.ToDictionary(scenario => scenario.Descriptor.Id, StringComparer.Ordinal);

        var missing = Packages
            .Select(package => package.Id)
            .Where(id => !scenarios.Any(scenario =>
                string.Equals(scenario.Descriptor.PackageId, id, StringComparison.Ordinal)))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"No scenarios exist for these packages: {string.Join(", ", missing)}");
        }
    }

    /// <summary>Every scenario, in navigation order.</summary>
    public IReadOnlyList<Scenario> All { get; }

    /// <summary>Packages in the order the sidebar renders them.</summary>
    public static IReadOnlyList<PackageDescriptor> Packages { get; } =
    [
        new("Pinqponq.Identity", "Identity", "Identity & Auth",
            "JWT (jti/revocation), refresh rotate + reuse/family revoke, PBKDF2."),
        new("Pinqponq.Identity.Otp", "Identity.Otp", "Identity & Auth",
            "OTP + HashPepper, channel-optional sender, rate limit, attempts/TTL."),
        new("Pinqponq.Auth.Totp", "Auth.Totp", "Identity & Auth",
            "RFC 6238 TOTP, ValidateAsync replay store, otpauth:// URI."),
        new("Pinqponq.Auth.Sso.Abstractions", "Auth.Sso.Abstractions", "Identity & Auth",
            "Dependency-free contract package for external identity providers."),
        new("Pinqponq.Auth.Sso.Google", "Auth.Sso.Google", "Identity & Auth",
            "Google id_token verification (ClientIds, email_verified)."),
        new("Pinqponq.Cache", "Cache", "Infrastructure",
            "Redis get/set, fencing token / TryExtendAsync, health-check."),
        new("Pinqponq.Database.Postgres", "Database.Postgres", "Infrastructure",
            "Npgsql connection factory, Polly retry, health-check."),
        new("Pinqponq.Database.Mongo", "Database.Mongo", "Infrastructure",
            "MongoDB client and health-check."),
        new("Pinqponq.Database.Mssql", "Database.Mssql", "Infrastructure",
            "SQL Server connection factory, transient error classification, health-check."),
        new("Pinqponq.Sms", "Sms", "Communication",
            "NetGSM GET/RestV2, HTTPS, AllowNoOp, Polly retry, job rejection."),
        new("Pinqponq.Mail", "Mail", "Communication",
            "SMTP, multiple recipients, AttachmentRoot path jail."),
        new("Pinqponq.Messaging.RabbitMq", "Messaging.RabbitMq", "Messaging",
            "Publish/consume, DLX, DLX-disabled drop, publish retry."),
        new("Pinqponq.ErrorHandling", "ErrorHandling", "Cross-cutting",
            "Global exception middleware, standard error body, and structured log record."),
    ];

    /// <summary>Looks up a scenario by id.</summary>
    public Scenario Get(string id) =>
        _byId.TryGetValue(id, out var scenario)
            ? scenario
            : throw new KeyNotFoundException($"Unknown scenario: {id}");

    /// <summary>Whether a scenario id exists.</summary>
    public bool Contains(string id) => _byId.ContainsKey(id);
}
