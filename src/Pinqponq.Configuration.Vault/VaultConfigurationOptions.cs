namespace Pinqponq.Configuration.Vault;

/// <summary>
/// Settings for loading one Vault KV v2 record into the application configuration.
/// </summary>
public sealed class VaultConfigurationOptions
{
    /// <summary>
    /// Configuration section the settings are read from when no section name is given.
    /// </summary>
    public const string DEFAULT_SECTION_NAME = "Vault";

    /// <summary>
    /// Vault server address, e.g. https://vault.example.com:8200. Falls back to the VAULT_ADDR environment variable.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Vault token. Falls back to the VAULT_TOKEN environment variable, then to the token file the Vault CLI
    /// writes on <c>vault login</c> (~/.vault-token).
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Vault Enterprise namespace; leave empty on Vault Community.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// KV version 2 secrets engine mount that holds the record, e.g. apps.
    /// </summary>
    public string? Mount { get; set; }

    /// <summary>
    /// Path of the record under the mount, e.g. my-product/my-service.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Timeout of a single request to Vault.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many times the record is requested before startup fails when Vault is unreachable or answers with a server error.
    /// </summary>
    public int MaxAttemptCount { get; set; } = 3;

    /// <summary>
    /// Wait between two attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Handler used for the requests to Vault, e.g. to trust a private certificate authority. The default handler is used when null.
    /// </summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }
}
