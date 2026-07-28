using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Resolves the key material for both signing and validation from a single
/// <see cref="JwtOptions"/> instance, keeping the two sides consistent.
/// RSA keys are imported once and cached for the lifetime of this resolver.
/// </summary>
public sealed class JwtSigningKeyResolver : IDisposable
{
    private const int MinimumHmacKeyBytes = 32; // 256 bits, required for HMAC-SHA256.

    private readonly JwtOptions _options;
    private readonly object _gate = new();
    private SymmetricSecurityKey? _symmetricKey;
    private RSA? _signingRsa;
    private RSA? _validationRsa;
    private RsaSecurityKey? _signingKey;
    private RsaSecurityKey? _validationKey;
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _options.Algorithm switch
        {
            JwtSigningAlgorithm.HmacSha256 =>
                new SigningCredentials(GetOrCreateSymmetricKey(), SecurityAlgorithms.HmacSha256),
            JwtSigningAlgorithm.RsaSha256 =>
                new SigningCredentials(GetOrCreateSigningKey(), SecurityAlgorithms.RsaSha256),
            _ => throw UnsupportedAlgorithm(),
        };
    }

    /// <summary>Builds the key used to validate incoming tokens.</summary>
    public SecurityKey CreateValidationKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _options.Algorithm switch
        {
            JwtSigningAlgorithm.HmacSha256 => GetOrCreateSymmetricKey(),
            JwtSigningAlgorithm.RsaSha256 => GetOrCreateValidationKey(),
            _ => throw UnsupportedAlgorithm(),
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _signingRsa?.Dispose();
            _validationRsa?.Dispose();
            _signingRsa = null;
            _validationRsa = null;
            _signingKey = null;
            _validationKey = null;
            _symmetricKey = null;
        }
    }

    private SymmetricSecurityKey GetOrCreateSymmetricKey()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_symmetricKey is not null)
            {
                return _symmetricKey;
            }

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

            _symmetricKey = new SymmetricSecurityKey(bytes);
            return _symmetricKey;
        }
    }

    private RsaSecurityKey GetOrCreateSigningKey()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return EnsureSigningKeyUnlocked();
        }
    }

    private RsaSecurityKey GetOrCreateValidationKey()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_validationKey is not null)
            {
                return _validationKey;
            }

            var pem = _options.RsaPublicKeyPem ?? _options.RsaPrivateKeyPem;
            if (string.IsNullOrWhiteSpace(pem))
            {
                throw new InvalidOperationException(
                    $"Either {nameof(JwtOptions.RsaPublicKeyPem)} or " +
                    $"{nameof(JwtOptions.RsaPrivateKeyPem)} is required to validate RSA tokens.");
            }

            // Same PEM material as signing → reuse the cached signing key.
            if (string.Equals(pem, _options.RsaPrivateKeyPem, StringComparison.Ordinal))
            {
                _validationKey = EnsureSigningKeyUnlocked();
                return _validationKey;
            }

            _validationRsa = ImportRsa(pem);
            _validationKey = new RsaSecurityKey(_validationRsa);
            return _validationKey;
        }
    }

    private RsaSecurityKey EnsureSigningKeyUnlocked()
    {
        if (_signingKey is not null)
        {
            return _signingKey;
        }

        if (string.IsNullOrWhiteSpace(_options.RsaPrivateKeyPem))
        {
            throw new InvalidOperationException(
                $"{nameof(JwtOptions.RsaPrivateKeyPem)} is required to sign RSA tokens.");
        }

        _signingRsa = ImportRsa(_options.RsaPrivateKeyPem);
        _signingKey = new RsaSecurityKey(_signingRsa);
        return _signingKey;
    }

    private static RSA ImportRsa(string pem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048)
            {
                throw new InvalidOperationException("RSA key size must be at least 2048 bits.");
            }

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
