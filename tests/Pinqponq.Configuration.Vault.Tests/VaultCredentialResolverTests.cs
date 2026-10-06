using Xunit;

namespace Pinqponq.Configuration.Vault.Tests;

public sealed class VaultCredentialResolverTests
{
    [Fact]
    public void ResolveToken_PrefersConfiguredTokenOverEnvironmentAndFile()
    {
        using var userProfile = new TemporaryUserProfile(tokenFileContent: "file-token");
        var resolver = CreateResolver(userProfile, environmentToken: "environment-token");

        var token = resolver.ResolveToken(" configured-token ");

        Assert.Equal(new VaultToken("configured-token", VaultTokenSource.Configuration), token);
    }

    [Fact]
    public void ResolveToken_FallsBackToEnvironmentVariable()
    {
        using var userProfile = new TemporaryUserProfile(tokenFileContent: "file-token");
        var resolver = CreateResolver(userProfile, environmentToken: "environment-token");

        var token = resolver.ResolveToken(configuredToken: null);

        Assert.Equal(new VaultToken("environment-token", VaultTokenSource.EnvironmentVariable), token);
    }

    [Fact]
    public void ResolveToken_FallsBackToVaultCliTokenFile_TrimmingTheTrailingNewline()
    {
        using var userProfile = new TemporaryUserProfile(tokenFileContent: "file-token\n");
        var resolver = CreateResolver(userProfile);

        var token = resolver.ResolveToken(configuredToken: "  ");

        Assert.Equal(new VaultToken("file-token", VaultTokenSource.TokenFile), token);
    }

    [Fact]
    public void ResolveToken_WhenNoTokenExistsAnywhere_ThrowsPointingToVaultLogin()
    {
        using var userProfile = new TemporaryUserProfile();
        var resolver = CreateResolver(userProfile);

        Action resolve = () => resolver.ResolveToken(configuredToken: null);

        var exception = Assert.ThrowsAny<VaultConfigurationException>(resolve);
        Assert.Contains("vault login", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VAULT_TOKEN", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveToken_WhenUserProfileIsUnknown_SkipsTheTokenFile()
    {
        var resolver = new VaultCredentialResolver(_ => null, userProfileDirectory: null);

        Action resolve = () => resolver.ResolveToken(configuredToken: null);

        Assert.ThrowsAny<VaultConfigurationException>(resolve);
    }

    [Fact]
    public void ResolveAddress_PrefersConfiguredAddress()
    {
        var resolver = new VaultCredentialResolver(_ => "http://from-environment:8200", userProfileDirectory: null);

        Assert.Equal("http://configured:8200", resolver.ResolveAddress(" http://configured:8200 "));
    }

    [Fact]
    public void ResolveAddress_FallsBackToVaultAddrEnvironmentVariable()
    {
        var resolver = new VaultCredentialResolver(
            variableName => variableName == VaultCredentialResolver.ADDRESS_ENVIRONMENT_VARIABLE ? "http://from-environment:8200" : null,
            userProfileDirectory: null);

        Assert.Equal("http://from-environment:8200", resolver.ResolveAddress(configuredAddress: null));
    }

    [Fact]
    public void ResolveAddress_WhenNoAddressExists_ThrowsNamingBothSources()
    {
        var resolver = new VaultCredentialResolver(_ => null, userProfileDirectory: null);

        var resolve = () => resolver.ResolveAddress(configuredAddress: null);

        var exception = Assert.ThrowsAny<VaultConfigurationException>(resolve);
        Assert.Contains("Address", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VAULT_ADDR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static VaultCredentialResolver CreateResolver(TemporaryUserProfile userProfile, string? environmentToken = null)
    {
        return new VaultCredentialResolver(
            variableName => variableName == VaultCredentialResolver.TOKEN_ENVIRONMENT_VARIABLE ? environmentToken : null,
            userProfile.DirectoryPath);
    }
}
