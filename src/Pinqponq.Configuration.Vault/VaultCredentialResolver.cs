namespace Pinqponq.Configuration.Vault;

/// <summary>
/// Finds the Vault address and token in the same places the Vault CLI looks, so a developer who ran
/// <c>vault login</c> needs no per-project setup.
/// </summary>
internal sealed class VaultCredentialResolver
{
    internal const string ADDRESS_ENVIRONMENT_VARIABLE = "VAULT_ADDR";
    internal const string TOKEN_ENVIRONMENT_VARIABLE = "VAULT_TOKEN";
    internal const string TOKEN_FILE_NAME = ".vault-token";

    private readonly Func<string, string?> _readEnvironmentVariable;
    private readonly string? _userProfileDirectory;

    public VaultCredentialResolver(Func<string, string?> readEnvironmentVariable, string? userProfileDirectory)
    {
        _readEnvironmentVariable = readEnvironmentVariable;
        _userProfileDirectory = userProfileDirectory;
    }

    public static VaultCredentialResolver CreateDefault()
    {
        return new VaultCredentialResolver(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public string ResolveAddress(string? configuredAddress)
    {
        if (!string.IsNullOrWhiteSpace(configuredAddress)) return configuredAddress.Trim();

        var environmentAddress = _readEnvironmentVariable(ADDRESS_ENVIRONMENT_VARIABLE);
        if (!string.IsNullOrWhiteSpace(environmentAddress)) return environmentAddress.Trim();

        throw new VaultConfigurationException(
            $"Vault address is not configured. Set the Address setting or the {ADDRESS_ENVIRONMENT_VARIABLE} environment variable.");
    }

    public VaultToken ResolveToken(string? configuredToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredToken))
        {
            return new VaultToken(configuredToken.Trim(), VaultTokenSource.Configuration);
        }

        var environmentToken = _readEnvironmentVariable(TOKEN_ENVIRONMENT_VARIABLE);
        if (!string.IsNullOrWhiteSpace(environmentToken))
        {
            return new VaultToken(environmentToken.Trim(), VaultTokenSource.EnvironmentVariable);
        }

        var fileToken = ReadTokenFile();
        if (!string.IsNullOrWhiteSpace(fileToken))
        {
            return new VaultToken(fileToken.Trim(), VaultTokenSource.TokenFile);
        }

        throw new VaultConfigurationException(
            $"No Vault token was found. Run 'vault login', or set the Token setting or the {TOKEN_ENVIRONMENT_VARIABLE} environment variable.");
    }

    private string? ReadTokenFile()
    {
        if (string.IsNullOrWhiteSpace(_userProfileDirectory)) return null;

        var tokenFilePath = Path.Combine(_userProfileDirectory, TOKEN_FILE_NAME);
        return File.Exists(tokenFilePath) ? File.ReadAllText(tokenFilePath) : null;
    }
}

internal enum VaultTokenSource
{
    Configuration,
    EnvironmentVariable,
    TokenFile
}

internal readonly record struct VaultToken(string Value, VaultTokenSource Source);
