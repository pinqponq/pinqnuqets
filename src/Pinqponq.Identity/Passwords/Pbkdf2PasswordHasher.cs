using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.Passwords;

/// <summary>
/// PBKDF2-based <see cref="IPasswordHasher"/> that wraps ASP.NET Core's
/// <see cref="PasswordHasher{TUser}"/>. The PBKDF2 hasher does not use the user object,
/// so a shared sentinel instance is passed to it.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>Maximum accepted password length to mitigate hashing DoS.</summary>
    public const int MaxPasswordLength = 1024;

    private static readonly object User = new();

    private readonly PasswordHasher<object> _inner;

    /// <summary>Creates a hasher with ASP.NET Core's default PBKDF2 parameters.</summary>
    public Pbkdf2PasswordHasher()
        : this(new PasswordHasher<object>())
    {
    }

    /// <summary>Creates a hasher with custom PBKDF2 parameters.</summary>
    public Pbkdf2PasswordHasher(PasswordHasherOptions options)
        : this(new PasswordHasher<object>(Options.Create(options)))
    {
    }

    private Pbkdf2PasswordHasher(PasswordHasher<object> inner) => _inner = inner;

    /// <inheritdoc />
    public string Hash(string password)
    {
        ValidatePassword(password);
        return _inner.HashPassword(User, password);
    }

    /// <inheritdoc />
    public PasswordVerificationOutcome Verify(string hashedPassword, string providedPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(hashedPassword);
        ValidatePassword(providedPassword);

        return _inner.VerifyHashedPassword(User, hashedPassword, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessRehashNeeded,
            _ => PasswordVerificationOutcome.Failed,
        };
    }

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > MaxPasswordLength)
        {
            throw new ArgumentException(
                $"Password must not exceed {MaxPasswordLength} characters.",
                nameof(password));
        }
    }
}
