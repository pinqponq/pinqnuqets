using Google.Apis.Auth.OAuth2;

namespace Pinqponq.Push.Fcm;

internal static class FcmCredentialLoader
{
    internal static GoogleCredential Load(FcmOptions options)
    {
        var serviceAccountCredential = string.IsNullOrWhiteSpace(options.CredentialJson)
            ? CredentialFactory.FromFile<ServiceAccountCredential>(ResolveCredentialPath(options.CredentialPath!))
            : CredentialFactory.FromJson<ServiceAccountCredential>(options.CredentialJson);
        return serviceAccountCredential.ToGoogleCredential();
    }

    /// <summary>A rooted <paramref name="credentialPath"/> is returned as it is: <see cref="Path.Combine(string, string)"/> drops the base for it.</summary>
    internal static string ResolveCredentialPath(string credentialPath) =>
        Path.Combine(AppContext.BaseDirectory, credentialPath);
}
