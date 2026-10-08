using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace Pinqponq.Configuration.Vault;

/// <summary>
/// Registers Vault as a configuration source.
/// </summary>
public static class VaultConfigurationExtensions
{
    private const string REQUEST_TIMEOUT_SECONDS_KEY = "RequestTimeoutSeconds";

    /// <summary>
    /// Loads the Vault record described by the given configuration section on top of the sources added so far.
    /// The section holds <c>Address</c>, <c>Mount</c> and <c>Path</c>, and optionally <c>Token</c>, <c>Namespace</c>
    /// and <c>RequestTimeoutSeconds</c>.
    /// </summary>
    public static IConfigurationManager AddPinqponqVault(
        this IConfigurationManager configuration,
        string sectionName = VaultConfigurationOptions.DEFAULT_SECTION_NAME)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var vaultOptions = ReadOptions(configuration.GetSection(sectionName));
        EnsureConfigured(vaultOptions.Mount, sectionName, nameof(VaultConfigurationOptions.Mount));
        EnsureConfigured(vaultOptions.Path, sectionName, nameof(VaultConfigurationOptions.Path));

        configuration.Add(new VaultConfigurationSource { Options = vaultOptions });
        return configuration;
    }

    /// <summary>
    /// Loads a Vault record with settings supplied in code.
    /// </summary>
    public static IConfigurationBuilder AddPinqponqVault(
        this IConfigurationBuilder configurationBuilder,
        Action<VaultConfigurationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var vaultOptions = new VaultConfigurationOptions();
        configureOptions(vaultOptions);

        return configurationBuilder.Add(new VaultConfigurationSource { Options = vaultOptions });
    }

    private static VaultConfigurationOptions ReadOptions(IConfigurationSection vaultSection)
    {
        var vaultOptions = new VaultConfigurationOptions
        {
            Address = vaultSection[nameof(VaultConfigurationOptions.Address)],
            Token = vaultSection[nameof(VaultConfigurationOptions.Token)],
            Namespace = vaultSection[nameof(VaultConfigurationOptions.Namespace)],
            Mount = vaultSection[nameof(VaultConfigurationOptions.Mount)],
            Path = vaultSection[nameof(VaultConfigurationOptions.Path)]
        };

        var requestTimeoutSetting = vaultSection[REQUEST_TIMEOUT_SECONDS_KEY];
        if (string.IsNullOrWhiteSpace(requestTimeoutSetting)) return vaultOptions;

        var isValidTimeout = int.TryParse(requestTimeoutSetting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestTimeoutSeconds)
            && requestTimeoutSeconds > 0;
        if (!isValidTimeout)
        {
            throw new VaultConfigurationException($"{vaultSection.Path}:{REQUEST_TIMEOUT_SECONDS_KEY} must be a positive whole number of seconds.");
        }

        vaultOptions.RequestTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds);
        return vaultOptions;
    }

    private static void EnsureConfigured(string? settingValue, string sectionName, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(settingValue)) return;

        throw new VaultConfigurationException($"{sectionName}:{settingName} is required to load configuration from Vault.");
    }
}
