namespace Pinqponq.Configuration.Vault.Tests;

/// <summary>
/// A throwaway directory standing in for the user profile, optionally holding a Vault CLI token file.
/// </summary>
internal sealed class TemporaryUserProfile : IDisposable
{
    public TemporaryUserProfile(string? tokenFileContent = null)
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"pinqponq-vault-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);

        if (tokenFileContent is not null)
        {
            File.WriteAllText(Path.Combine(DirectoryPath, VaultCredentialResolver.TOKEN_FILE_NAME), tokenFileContent);
        }
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
        Directory.Delete(DirectoryPath, recursive: true);
    }
}
