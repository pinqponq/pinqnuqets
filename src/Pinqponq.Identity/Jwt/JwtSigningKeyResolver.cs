using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Resolves the key material for both signing and validation from a single
/// <see cref="JwtOptions"/> instance, keeping the two sides consistent.
/// </summary>
public sealed class JwtSigningKeyResolver
{
    private const int MinimumHmacKeyBytes = 32; // 256 bits, required for HMAC-SHA256.

    private readonly JwtOptions _options;

    /// <summary>Creates a resolver for the given options.</summary>
    public JwtSigningKeyResolver(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>The algorithm identifier passed to the token handler.</summary>
    public string Algorithm => _options.Algorithm switch
    {
        JwtSigningAlgorithm.HmacSha256 => SecurityAlgorithms.HmacSha256,
        JwtSigningAlgorithm.RsaSha256 => SecurityAlgorithms.RsaSha256,
        _ => throw UnsupportedAlgorithm(),
    };

    /// <summary>Builds the credentials used to sign newly issued tokens.</summary>
    public SigningCredentials CreateSigningCredentials()
    {
        switch (_options.Algorithm)
        {
            case JwtSigningAlgorithm.HmacSha256:
                return new SigningCredentials(CreateSymmetricKey(), SecurityAlgorithms.HmacSha256);

            case JwtSigningAlgorithm.RsaSha256:
                if (string.IsNullOrWhiteSpace(_options.RsaPrivateKeyPem))
                {
                    throw new InvalidOperationException(
                        $"{nameof(JwtOptions.RsaPrivateKeyPem)} is required to sign RSA tokens.");
                }

                return new SigningCredentials(
                    new RsaSecurityKey(ImportRsa(_options.RsaPrivateKeyPem)),
                    SecurityAlgorithms.RsaSha256);

            default:
                throw UnsupportedAlgorithm();
        }
    }

    /// <summary>Builds the key used to validate incoming tokens.</summary>
    public SecurityKey CreateValidationKey()
    {
        switch (_options.Algorithm)
        {
            case JwtSigningAlgorithm.HmacSha256:
                return CreateSymmetricKey();

            case JwtSigningAlgorithm.RsaSha256:
                var pem = _options.RsaPublicKeyPem ?? _options.RsaPrivateKeyPem;
                if (string.IsNullOrWhiteSpace(pem))
                {
                    throw new InvalidOperationException(
                        $"Either {nameof(JwtOptions.RsaPublicKeyPem)} or " +
                        $"{nameof(JwtOptions.RsaPrivateKeyPem)} is required to validate RSA tokens.");
                }

                return new RsaSecurityKey(ImportRsa(pem));

            default:
                throw UnsupportedAlgorithm();
        }
    }

    private SymmetricSecurityKey CreateSymmetricKey()
    {
        if (string.IsNullOrEmpty(_options.SymmetricKey))
        {
            throw new InvalidOperationException(
                $"{nameof(JwtOptions.SymmetricKey)} is required for HMAC signing.");
        }

        var bytes = Encoding.UTF8.GetBytes(_options.SymmetricKey);
        if (bytes.Length < MinimumHmacKeyBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(JwtOptions.SymmetricKey)} must be at least {MinimumHmacKeyBytes} bytes " +
                $"({MinimumHmacKeyBytes * 8} bits) for HMAC-SHA256.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    private static RSA ImportRsa(string pem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private InvalidOperationException UnsupportedAlgorithm() =>
        new($"Unsupported signing algorithm '{_options.Algorithm}'.");
}
