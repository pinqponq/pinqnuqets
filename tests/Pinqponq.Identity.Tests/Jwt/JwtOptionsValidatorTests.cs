using System.Security.Cryptography;
using FluentAssertions;
using Pinqponq.Identity.Jwt;
using Xunit;

namespace Pinqponq.Identity.Tests.Jwt;

public sealed class JwtOptionsValidatorTests
{
    private readonly JwtOptionsValidator _validator = new();

    [Fact]
    public void Rsa_key_smaller_than_2048_fails()
    {
        using var rsa = RSA.Create(1024);
        var options = new JwtOptions
        {
            Issuer = "pinqponq",
            Audience = "clients",
            Algorithm = JwtSigningAlgorithm.RsaSha256,
            RsaPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
        };

        _validator.Validate(null, options).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Rsa_key_2048_succeeds()
    {
        using var rsa = RSA.Create(2048);
        var options = new JwtOptions
        {
            Issuer = "pinqponq",
            Audience = "clients",
            Algorithm = JwtSigningAlgorithm.RsaSha256,
            RsaPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
        };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }
}
