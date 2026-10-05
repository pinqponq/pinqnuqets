using FluentAssertions;
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

        token.Should().Be(new VaultToken("configured-token", VaultTokenSource.Configuration));
    }

    [Fact]
    public void ResolveToken_FallsBackToEnvironmentVariable()
    {
        using var userProfile = new TemporaryUserProfile(tokenFileContent: "file-token");
        var resolver = CreateResolver(userProfile, environmentToken: "environment-token");

        var token = resolver.ResolveToken(configuredToken: null);

        token.Should().Be(new VaultToken("environment-token", VaultTokenSource.EnvironmentVariable));
    }

    [Fact]
    public void ResolveToken_FallsBackToVaultCliTokenFile_TrimmingTheTrailingNewline()
    {
        using var userProfile = new TemporaryUserProfile(tokenFileContent: "file-token\n");
        var resolver = CreateResolver(userProfile);

        var token = resolver.ResolveToken(configuredToken: "  ");

        token.Should().Be(new VaultToken("file-token", VaultTokenSource.TokenFile));
    }

    [Fact]
    public void ResolveToken_WhenNoTokenExistsAnywhere_ThrowsPointingToVaultLogin()
    {
        using var userProfile = new TemporaryUserProfile();
        var resolver = CreateResolver(userProfile);

        var resolve = () => resolver.ResolveToken(configuredToken: null);

        resolve.Should().Throw<VaultConfigurationException>().WithMessage("*vault login*VAULT_TOKEN*");
    }

    [Fact]
    public void ResolveToken_WhenUserProfileIsUnknown_SkipsTheTokenFile()
    {
        var resolver = new VaultCredentialResolver(_ => null, userProfileDirectory: null);

        var resolve = () => resolver.ResolveToken(configuredToken: null);

        resolve.Should().Throw<VaultConfigurationException>();
    }

    [Fact]
    public void ResolveAddress_PrefersConfiguredAddress()
    {
        var resolver = new VaultCredentialResolver(_ => "http://from-environment:8200", userProfileDirectory: null);

        resolver.ResolveAddress(" http://configured:8200 ").Should().Be("http://configured:8200");
    }

    [Fact]
    public void ResolveAddress_FallsBackToVaultAddrEnvironmentVariable()
    {
        var resolver = new VaultCredentialResolver(
            variableName => variableName == VaultCredentialResolver.ADDRESS_ENVIRONMENT_VARIABLE ? "http://from-environment:8200" : null,
            userProfileDirectory: null);

        resolver.ResolveAddress(configuredAddress: null).Should().Be("http://from-environment:8200");
    }

    [Fact]
    public void ResolveAddress_WhenNoAddressExists_ThrowsNamingBothSources()
    {
        var resolver = new VaultCredentialResolver(_ => null, userProfileDirectory: null);

        var resolve = () => resolver.ResolveAddress(configuredAddress: null);

        resolve.Should().Throw<VaultConfigurationException>().WithMessage("*Address*VAULT_ADDR*");
    }

    private static VaultCredentialResolver CreateResolver(TemporaryUserProfile userProfile, string? environmentToken = null)
    {
        return new VaultCredentialResolver(
            variableName => variableName == VaultCredentialResolver.TOKEN_ENVIRONMENT_VARIABLE ? environmentToken : null,
            userProfile.DirectoryPath);
    }
}
