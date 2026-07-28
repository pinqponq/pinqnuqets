using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Pinqponq.Sms;

/// <summary>
/// NetGSM <see cref="ISmsSender"/> supporting legacy GET query auth and REST v2 POST
/// with Basic Auth.
/// </summary>
/// <remarks>
/// When <see cref="SmsTransport.GetQuery"/> is used with an empty
/// <see cref="SmsOptions.ApiUrl"/> and <see cref="SmsOptions.AllowNoOp"/> is true, sending
/// is a no-op. GetQuery credentials travel on the query string — do not log request URLs.
/// </remarks>
public sealed class NetGsmSmsSender : ISmsSender
{
    /// <summary>The named <see cref="HttpClient"/> registered for this sender.</summary>
    public const string HttpClientName = "Pinqponq.Sms.NetGsm";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
                ShouldHandle = args =>
                {
                    // Business rejects are permanent — do not retry.
                    if (args.Outcome.Exception is NetGsmRejectedException)
                    {
                        return PredicateResult.False();
                    }

                    if (args.Outcome.Exception is HttpRequestException)
                    {
                        return PredicateResult.True();
                    }

                    // Retry timeouts, but never caller-initiated cancellation.
                    if (args.Outcome.Exception is TaskCanceledException
                        && !args.Context.CancellationToken.IsCancellationRequested)
                    {
                        return PredicateResult.True();
                    }

                    return PredicateResult.False();
                },
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_options.Transport == SmsTransport.GetQuery && string.IsNullOrWhiteSpace(_options.ApiUrl))
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

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            throw new ArgumentException("Message text is required.", nameof(message));
        }

        var phoneDigits = new string(message.To.Where(char.IsDigit).ToArray());
        if (phoneDigits.Length == 0)
        {
            throw new ArgumentException("Recipient must contain at least one digit.", nameof(message));
        }

        if (_options.Transport == SmsTransport.RestV2)
        {
            await SendRestV2Async(phoneDigits, message.Text, cancellationToken).ConfigureAwait(false);
            return;
        }

        var url = BuildRequestUrl(phoneDigits, message.Text);

        await _pipeline.ExecuteAsync(
            async token =>
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.GetAsync(url, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var body = (await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Trim();
                if (!IsNetGsmSuccess(body))
                {
                    throw new NetGsmRejectedException(body);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRestV2Async(
        string phoneDigits,
        string text,
        CancellationToken cancellationToken)
    {
        var apiUrl = string.IsNullOrWhiteSpace(_options.ApiUrl)
            ? SmsOptions.DefaultRestV2ApiUrl
            : _options.ApiUrl!;

        var payload = new
        {
            msgheader = _options.MsgHeader ?? string.Empty,
            messages = new[]
            {
                new { msg = text, no = phoneDigits },
            },
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.UserCode}:{_options.Password}"));

        await _pipeline.ExecuteAsync(
            async token =>
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var body = (await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Trim();
                if (!IsNetGsmSuccess(body))
                {
                    throw new NetGsmRejectedException(body);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// NetGSM returns HTTP 200 for many business failures; success bodies start with
    /// <c>00</c> (optionally followed by a job id), or JSON with a <c>code</c> of
    /// <c>00</c>.
    /// </summary>
    internal static bool IsNetGsmSuccess(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        if (body.StartsWith("00", StringComparison.Ordinal)
            && (body.Length == 2 || char.IsWhiteSpace(body[2]) || body[2] is ' ' or '\t'))
        {
            return true;
        }

        // Plain "00" already covered; also accept JSON {"code":"00",...} / {"code":00}.
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("code", out var code))
            {
                return code.ValueKind switch
                {
                    JsonValueKind.String => code.GetString() is "00",
                    JsonValueKind.Number => code.TryGetInt32(out var n) && n == 0,
                    _ => false,
                };
            }

            // Some responses use a root string "00".
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return doc.RootElement.GetString() is "00";
            }
        }
        catch (JsonException)
        {
            // Fall through — treat as failure.
        }

        return false;
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
