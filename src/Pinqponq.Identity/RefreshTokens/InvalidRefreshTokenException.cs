namespace Pinqponq.Identity.RefreshTokens;

/// <summary>
/// Thrown when a presented refresh token is unknown, expired or already revoked.
/// </summary>
public sealed class InvalidRefreshTokenException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public InvalidRefreshTokenException()
        : base("The refresh token is invalid, expired or has been revoked.")
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    public InvalidRefreshTokenException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a custom message and inner exception.</summary>
    public InvalidRefreshTokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
