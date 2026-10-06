using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.Serialization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pinqponq.LiveKit.Server.Twirp;

/// <summary>
/// Sends Twirp JSON requests to LiveKit, authorized with a short-lived token carrying only the grant the call needs.
/// </summary>
internal sealed class TwirpClient
{
    private const string TWIRP_PATH_PREFIX = "twirp";
    private const string BEARER_SCHEME = "Bearer";

    private static readonly TimeSpan ServiceTokenTtl = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILiveKitCredentialsProvider _credentialsProvider;
    private readonly TimeProvider _timeProvider;

    public TwirpClient(
        HttpClient httpClient,
        LiveKitServerOptions serverOptions,
        ILiveKitCredentialsProvider credentialsProvider,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _baseUrl = LiveKitServerUrl.ToHttpBaseUrl(serverOptions.Url);
        _credentialsProvider = credentialsProvider;
        _timeProvider = timeProvider;
    }

    public async Task<TResponse> Call<TRequest, TResponse>(
        string serviceName,
        string methodName,
        TRequest request,
        VideoGrant grant,
        CancellationToken cancellationToken)
    {
        var serviceToken = await CreateServiceToken(grant, cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/{TWIRP_PATH_PREFIX}/{serviceName}/{methodName}");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(BEARER_SCHEME, serviceToken.Value);
        httpRequest.Content = JsonContent.Create(request, options: ProtoJson.RequestOptions);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw CreateApiException(httpResponse, responseBody, serviceName, methodName);
        }

        return ProtoJson.Deserialize<TResponse>(responseBody);
    }

    private async Task<LiveKitAccessToken> CreateServiceToken(VideoGrant grant, CancellationToken cancellationToken)
    {
        var credentials = await _credentialsProvider.GetCredentials(cancellationToken);
        var tokenOptions = new AccessTokenOptions
        {
            VideoGrant = grant,
            Ttl = ServiceTokenTtl
        };

        return AccessTokenIssuer.Issue(tokenOptions, credentials, _timeProvider.GetUtcNow());
    }

    private static LiveKitApiException CreateApiException(
        HttpResponseMessage httpResponse,
        string responseBody,
        string serviceName,
        string methodName)
    {
        var twirpError = TryParseTwirpError(responseBody);
        var errorDetail = twirpError?.Msg ?? responseBody;
        var message = $"LiveKit {serviceName}/{methodName} failed with {(int)httpResponse.StatusCode}: {errorDetail}";
        return new LiveKitApiException(message, twirpError?.Code, httpResponse.StatusCode);
    }

    private static TwirpError? TryParseTwirpError(string responseBody)
    {
        try
        {
            var twirpError = JsonSerializer.Deserialize<TwirpError>(responseBody, ProtoJson.ResponseOptions);
            return string.IsNullOrEmpty(twirpError?.Code) ? null : twirpError;
        }
        catch (JsonException)
        {
            // Not a Twirp error body (e.g. an HTML page from a proxy); the raw body is reported in the exception message instead.
            return null;
        }
    }

    private sealed class TwirpError
    {
        public string? Code { get; init; }
        public string? Msg { get; init; }
    }
}
