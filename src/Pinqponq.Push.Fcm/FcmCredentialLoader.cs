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

    internal static string ResolveCredentialPath(string credentialPath) =>
        Path.IsPathRooted(credentialPath)
            ? credentialPath
            : Path.Combine(AppContext.BaseDirectory, credentialPath);
}
