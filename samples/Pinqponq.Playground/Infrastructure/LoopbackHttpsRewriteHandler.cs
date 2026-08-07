namespace Pinqponq.Playground.Infrastructure;

/// <summary>
/// Rewrites loopback HTTPS URLs to HTTP so <c>SmsOptions</c> can require HTTPS while the
/// console itself only listens on plain HTTP.
/// </summary>
internal sealed class LoopbackHttpsRewriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
        {
            request.RequestUri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttp }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
