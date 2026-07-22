namespace Pinqponq.Auth.Sso.Abstractions;

/// <summary>
/// Common contract implemented by external identity providers (Google, and future
/// providers). Implementations live in separate <c>Pinqponq.Auth.Sso.*</c> packages.
/// </summary>
public interface IExternalAuthProvider
{
    /// <summary>The provider name this implementation handles (e.g. <c>Google</c>).</summary>
    string ProviderName { get; }

    /// <summary>
    /// Authenticates the given request and returns the normalized external user profile.
    /// </summary>
    /// <param name="request">The id-token or authorization-code input.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<ExternalAuthResult> AuthenticateAsync(
        ExternalAuthRequest request,
        CancellationToken cancellationToken = default);
}
