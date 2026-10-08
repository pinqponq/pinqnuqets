using Microsoft.Extensions.Configuration;

namespace Pinqponq.Configuration.Vault;

/// <summary>
/// Configuration source that loads one Vault KV v2 record.
/// </summary>
public sealed class VaultConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// Settings that locate the record and authenticate to Vault.
    /// </summary>
    public required VaultConfigurationOptions Options { get; init; }

    internal VaultCredentialResolver CredentialResolver { get; init; } = VaultCredentialResolver.CreateDefault();

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new VaultConfigurationProvider(this);
    }
}
