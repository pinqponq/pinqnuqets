namespace Pinqponq.Auth.Sso.Abstractions;

/// <summary>
/// Outcome of an external authentication attempt.
/// </summary>
public sealed class ExternalAuthResult
{
    private ExternalAuthResult(bool succeeded, ExternalUserInfo? user, string? error)
    {
        Succeeded = succeeded;
        User = user;
        Error = error;
    }

    /// <summary>Whether authentication succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>The authenticated user profile when <see cref="Succeeded"/> is true.</summary>
    public ExternalUserInfo? User { get; }

    /// <summary>A human-readable error when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; }

    /// <summary>Creates a successful result.</summary>
    public static ExternalAuthResult Success(ExternalUserInfo user) =>
        new(true, user ?? throw new ArgumentNullException(nameof(user)), null);

    /// <summary>Creates a failed result.</summary>
    public static ExternalAuthResult Failure(string error) =>
        new(false, null, error);
}
