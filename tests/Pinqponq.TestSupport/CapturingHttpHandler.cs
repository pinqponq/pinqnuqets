using System.Net;
using System.Net.Http.Headers;

namespace Pinqponq.TestSupport;

/// <summary>Records outbound HTTP requests and returns a configurable response.</summary>
public sealed class CapturingHttpHandler : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<string?> _bodies = [];
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _content = "00";
    private string _mediaType = "text/plain";

    /// <summary>Captured requests in call order.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>Captured request bodies in call order (null when no content).</summary>
    public IReadOnlyList<string?> RequestBodies => _bodies;

    /// <summary>Last captured request, or null if none.</summary>
    public HttpRequestMessage? LastRequest => _requests.Count == 0 ? null : _requests[^1];

    /// <summary>Last captured request body, or null if none / empty content.</summary>
    public string? LastRequestBody => _bodies.Count == 0 ? null : _bodies[^1];

    /// <summary>Configures the response returned for subsequent requests.</summary>
    public CapturingHttpHandler RespondWith(
        HttpStatusCode statusCode,
        string content = "00",
        string mediaType = "text/plain")
    {
        _statusCode = statusCode;
        _content = content;
        _mediaType = mediaType;
        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        _requests.Add(request);
        _bodies.Add(body);

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(_mediaType);
        return response;
    }
}
