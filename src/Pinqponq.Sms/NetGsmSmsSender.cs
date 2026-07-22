using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Pinqponq.Sms;

/// <summary>
/// NetGSM-style <see cref="ISmsSender"/> that issues an HTTP GET with
/// <c>usercode/password/gsmno/message/msgheader</c> query parameters.
/// </summary>
public sealed class NetGsmSmsSender : ISmsSender
{
    /// <summary>The named <see cref="HttpClient"/> registered for this sender.</summary>
    public const string HttpClientName = "Pinqponq.Sms.NetGsm";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SmsOptions _options;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Creates the sender from configured options and the HTTP client factory.</summary>
    public NetGsmSmsSender(IHttpClientFactory httpClientFactory, IOptions<SmsOptions> options)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.RetryCount,
                Delay = _options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // No endpoint configured → sending is intentionally a no-op.
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.UserCode))
        {
            throw new InvalidOperationException("SmsOptions.UserCode is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("SmsOptions.Password is required.");
        }

        if (string.IsNullOrWhiteSpace(message.To))
        {
            throw new ArgumentException("Recipient is required.", nameof(message));
        }

        var phoneDigits = new string(message.To.Where(char.IsDigit).ToArray());
        if (phoneDigits.Length == 0)
        {
            return;
        }

        var url = BuildRequestUrl(phoneDigits, message.Text);

        await _pipeline.ExecuteAsync(
            async token =>
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.GetAsync(url, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            },
            cancellationToken).ConfigureAwait(false);
    }

    private string BuildRequestUrl(string phoneDigits, string text)
    {
        var parameters = new Dictionary<string, string>
        {
            ["usercode"] = _options.UserCode!,
            ["password"] = _options.Password!,
            ["gsmno"] = phoneDigits,
            ["message"] = text,
            ["msgheader"] = _options.MsgHeader ?? string.Empty,
        };

        var query = string.Join("&", parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return _options.ApiUrl!.TrimEnd('?', '&') + "?" + query;
    }
}
