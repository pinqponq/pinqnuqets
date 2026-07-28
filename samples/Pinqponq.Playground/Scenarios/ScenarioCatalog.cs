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
                $"Şu paketler için hiç senaryo yok: {string.Join(", ", missing)}");
        }
    }

    /// <summary>Every scenario, in navigation order.</summary>
    public IReadOnlyList<Scenario> All { get; }

    /// <summary>Packages in the order the sidebar renders them.</summary>
    public static IReadOnlyList<PackageDescriptor> Packages { get; } =
    [
        new("Pinqponq.Identity", "Identity", "Kimlik & Yetki",
            "JWT üretimi/doğrulaması, refresh token döngüsü, PBKDF2 parola hash'leme."),
        new("Pinqponq.Identity.Otp", "Identity.Otp", "Kimlik & Yetki",
            "Tek kullanımlık kod üretimi, mail/SMS kanal yönlendirmesi, deneme ve süre sınırı."),
        new("Pinqponq.Auth.Totp", "Auth.Totp", "Kimlik & Yetki",
            "RFC 6238 TOTP 2FA, otpauth:// provisioning URI, Base32 yardımcısı."),
        new("Pinqponq.Auth.Sso.Abstractions", "Auth.Sso.Abstractions", "Kimlik & Yetki",
            "Dış kimlik sağlayıcıları için bağımlılıksız sözleşme paketi."),
        new("Pinqponq.Auth.Sso.Google", "Auth.Sso.Google", "Kimlik & Yetki",
            "Google id_token doğrulaması."),
        new("Pinqponq.Cache", "Cache", "Altyapı",
            "Redis get/set, dağıtık kilit ve health-check."),
        new("Pinqponq.Database.Postgres", "Database.Postgres", "Altyapı",
            "Npgsql bağlantı fabrikası, Polly retry, health-check."),
        new("Pinqponq.Database.Mongo", "Database.Mongo", "Altyapı",
            "MongoDB istemcisi ve health-check."),
        new("Pinqponq.Database.Mssql", "Database.Mssql", "Altyapı",
            "SQL Server bağlantı fabrikası, geçici hata sınıflandırması, health-check."),
        new("Pinqponq.Sms", "Sms", "İletişim",
            "NetGSM uyumlu SMS gönderimi ve retry."),
        new("Pinqponq.Mail", "Mail", "İletişim",
            "SMTP mail gönderimi, çoklu alıcı ve ek dosya davranışı."),
        new("Pinqponq.Messaging.RabbitMq", "Messaging.RabbitMq", "Mesajlaşma",
            "Publish/consume, dead-letter yönlendirmesi, publish retry."),
        new("Pinqponq.ErrorHandling", "ErrorHandling", "Kesişen",
            "Global exception middleware, standart hata gövdesi ve yapılandırılmış log kaydı."),
    ];

    /// <summary>Looks up a scenario by id.</summary>
    public Scenario Get(string id) =>
        _byId.TryGetValue(id, out var scenario)
            ? scenario
            : throw new KeyNotFoundException($"Bilinmeyen senaryo: {id}");

    /// <summary>Whether a scenario id exists.</summary>
    public bool Contains(string id) => _byId.ContainsKey(id);
}
