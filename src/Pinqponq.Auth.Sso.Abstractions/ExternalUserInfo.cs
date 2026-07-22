namespace Pinqponq.Auth.Sso.Abstractions;

/// <summary>
/// Normalized user profile returned by an external identity provider.
/// </summary>
public sealed class ExternalUserInfo
{
    /// <summary>The provider-scoped unique user id (e.g. Google's <c>sub</c>).</summary>
    public required string Subject { get; init; }

    /// <summary>The provider name that produced this profile (e.g. <c>Google</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>The user's email address, if provided.</summary>
    public string? Email { get; init; }

    /// <summary>Whether the provider asserts the email is verified.</summary>
    public bool EmailVerified { get; init; }

    /// <summary>The user's full display name, if provided.</summary>
    public string? Name { get; init; }

    /// <summary>Given (first) name, if provided.</summary>
    public string? GivenName { get; init; }

    /// <summary>Family (last) name, if provided.</summary>
    public string? FamilyName { get; init; }

    /// <summary>Profile picture URL, if provided.</summary>
    public string? Picture { get; init; }
}
