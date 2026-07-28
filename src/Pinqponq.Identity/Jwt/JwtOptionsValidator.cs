using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.Jwt;

/// <summary>Validates <see cref="JwtOptions"/> at options bind / startup.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private const int MinimumHmacKeyBytes = 32;
    private const int MinimumRsaKeySize = 2048;
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            return ValidateOptionsResult.Fail($"{nameof(JwtOptions.Issuer)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            return ValidateOptionsResult.Fail($"{nameof(JwtOptions.Audience)} is required.");
        }

        if (options.Lifetime <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(JwtOptions.Lifetime)} must be positive.");
        }

        if (options.ClockSkew < TimeSpan.Zero || options.ClockSkew > MaxClockSkew)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(JwtOptions.ClockSkew)} must be between 0 and {MaxClockSkew.TotalMinutes} minutes.");
        }

        return options.Algorithm switch
        {
            JwtSigningAlgorithm.HmacSha256 => ValidateHmac(options),
            JwtSigningAlgorithm.RsaSha256 => ValidateRsa(options),
            _ => ValidateOptionsResult.Fail($"Unsupported algorithm '{options.Algorithm}'."),
        };
    }

    private static ValidateOptionsResult ValidateHmac(JwtOptions options)
    {
        if (string.IsNullOrEmpty(options.SymmetricKey))
        {
            return ValidateOptionsResult.Fail($"{nameof(JwtOptions.SymmetricKey)} is required for HMAC.");
        }

        if (Encoding.UTF8.GetByteCount(options.SymmetricKey) < MinimumHmacKeyBytes)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(JwtOptions.SymmetricKey)} must be at least {MinimumHmacKeyBytes} bytes.");
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult ValidateRsa(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RsaPrivateKeyPem))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(JwtOptions.RsaPrivateKeyPem)} is required to sign RSA tokens.");
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(options.RsaPrivateKeyPem);
            if (rsa.KeySize < MinimumRsaKeySize)
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(JwtOptions.RsaPrivateKeyPem)} must be at least {MinimumRsaKeySize} bits.");
            }
        }
        catch (Exception ex)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(JwtOptions.RsaPrivateKeyPem)} is not a valid RSA private key: {ex.Message}");
        }

        return ValidateOptionsResult.Success;
    }
}
