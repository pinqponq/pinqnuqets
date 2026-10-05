using System.Net;
using System.Text;

namespace Pinqponq.Configuration.Vault.Tests;

/// <summary>
/// Answers Vault requests from a queue. The provider calls Vault synchronously, so this overrides
/// <see cref="Send"/>, which the shared capturing handler does not.
/// </summary>
internal sealed class StubVaultHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responseFactories = new();

    public List<HttpRequestMessage> ReceivedRequests { get; } = [];

    public StubVaultHandler RespondWith(HttpStatusCode statusCode, string responseBody = "{}")
    {
        _responseFactories.Enqueue(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        return this;
    }

    public StubVaultHandler FailWith(Exception exception)
    {
        _responseFactories.Enqueue(() => throw exception);
        return this;
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ReceivedRequests.Add(request);
        return _responseFactories.Dequeue().Invoke();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Send(request, cancellationToken));
    }
}
