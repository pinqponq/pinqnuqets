using Pinqponq.Auth.Sso.Abstractions;

namespace Pinqponq.Auth.Sso.Firebase;

/// <summary>
/// Outcome of a Firebase sign-in: the shared <see cref="ExternalAuthResult"/> plus the sign-in method
/// Firebase reports for the user.
/// </summary>
public sealed class FirebaseAuthResult
{
    internal FirebaseAuthResult(ExternalAuthResult result, string? signInProvider)
    {
        Result = result;
        SignInProvider = signInProvider;
    }

    /// <summary>The shared authentication result.</summary>
    public ExternalAuthResult Result { get; }

    /// <summary>
    /// The token's <c>firebase.sign_in_provider</c> claim, for example <c>apple.com</c> or
    /// <c>google.com</c>. <see langword="null"/> when authentication failed or the claim is missing.
    /// </summary>
    public string? SignInProvider { get; }
}
