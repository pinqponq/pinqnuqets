namespace Pinqponq.LiveKit.Server;

internal static class LiveKitServerUrl
{
    // UriBuilder treats -1 as the scheme's default port, so wss://host becomes https://host without an explicit :443.
    private const int SCHEME_DEFAULT_PORT = -1;

    private static readonly Dictionary<string, string> HttpSchemeByScheme = new(StringComparer.OrdinalIgnoreCase)
    {
        [Uri.UriSchemeHttp] = Uri.UriSchemeHttp,
        [Uri.UriSchemeHttps] = Uri.UriSchemeHttps,
        [Uri.UriSchemeWs] = Uri.UriSchemeHttp,
        [Uri.UriSchemeWss] = Uri.UriSchemeHttps
    };

    public static bool IsValid(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && HttpSchemeByScheme.ContainsKey(uri.Scheme);

    public static string ToHttpBaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !HttpSchemeByScheme.TryGetValue(uri.Scheme, out var httpScheme))
        {
            throw new ArgumentException($"'{url}' is not a valid LiveKit server URL. Use an absolute http, https, ws or wss URL.", nameof(url));
        }

        var port = uri.IsDefaultPort ? SCHEME_DEFAULT_PORT : uri.Port;
        var httpUri = new UriBuilder(uri) { Scheme = httpScheme, Port = port }.Uri;
        return httpUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
