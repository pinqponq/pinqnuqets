namespace Pinqponq.Configuration.Vault;

/// <summary>
/// Thrown when the configuration record cannot be loaded from Vault. The message never contains a secret value or the token.
/// </summary>
public sealed class VaultConfigurationException : Exception
{
    public VaultConfigurationException(string message)
        : base(message)
    {
    }

    public VaultConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
