using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pinqponq.Playground.Infrastructure;

/// <summary>A message captured by MailHog, flattened for the console.</summary>
public sealed record CapturedMail(
    string Id,
    DateTimeOffset ReceivedAt,
    string From,
    IReadOnlyList<string> To,
    string Subject,
    string Body,
    string RawBody);

/// <summary>
/// Reads the MailHog inbox so mail the packages send is visible in the console.
/// </summary>
/// <remarks>
/// MailHog nests headers as <c>Content.Headers.Subject[0]</c> and leaves bodies in their
/// transfer encoding. Flattening and decoding here keeps that shape out of the frontend
/// and out of every scenario.
/// </remarks>
public sealed partial class MailHogClient(IHttpClientFactory httpClientFactory, DevStackManager stack)
{
    /// <summary>Named client used for MailHog's HTTP API.</summary>
    public const string HttpClientName = "Playground.MailHog";

    /// <summary>Whether the MailHog service is available right now.</summary>
    public bool IsAvailable => stack.IsReady(DevServiceIds.MailHog);

    /// <summary>Base URL of the MailHog HTTP API.</summary>
    public string ApiBaseUrl =>
        stack.Require(DevServiceIds.MailHog).Extra.TryGetValue("apiBaseUrl", out var url)
            ? url
            : throw new DevStackNotReadyException("MailHog API adresi çözülemedi.");

    /// <summary>Lists the most recent messages, newest first.</summary>
    public async Task<IReadOnlyList<CapturedMail>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client
            .GetAsync(new Uri($"{ApiBaseUrl}/api/v2/messages?limit={take}"), cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("items", out var items))
        {
            return [];
        }

        var messages = new List<CapturedMail>();
        foreach (var item in items.EnumerateArray())
        {
            messages.Add(Project(item));
        }

        return messages;
    }

    /// <summary>Deletes every captured message.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client
            .DeleteAsync(new Uri($"{ApiBaseUrl}/api/v1/messages"), cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    private static CapturedMail Project(JsonElement item)
    {
        var id = item.TryGetProperty("ID", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;

        var receivedAt = item.TryGetProperty("Created", out var created)
                         && DateTimeOffset.TryParse(
                             created.GetString(),
                             CultureInfo.InvariantCulture,
                             DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                             out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        var content = item.TryGetProperty("Content", out var contentElement) ? contentElement : default;
        var headers = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("Headers", out var headerElement)
            ? headerElement
            : default;

        var rawBody = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("Body", out var bodyElement)
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;

        var transferEncoding = FirstHeader(headers, "Content-Transfer-Encoding");
        var body = DecodeBody(rawBody, transferEncoding);

        return new CapturedMail(
            id,
            receivedAt,
            DecodeHeader(FirstHeader(headers, "From")),
            AllHeaderValues(headers, "To"),
            DecodeHeader(FirstHeader(headers, "Subject")),
            body,
            rawBody);
    }

    private static string? FirstHeader(JsonElement headers, string name) =>
        headers.ValueKind == JsonValueKind.Object
        && headers.TryGetProperty(name, out var values)
        && values.ValueKind == JsonValueKind.Array
        && values.GetArrayLength() > 0
            ? values[0].GetString()
            : null;

    private static IReadOnlyList<string> AllHeaderValues(JsonElement headers, string name)
    {
        if (headers.ValueKind != JsonValueKind.Object
            || !headers.TryGetProperty(name, out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. values.EnumerateArray()
            .Select(value => DecodeHeader(value.GetString()))
            .Where(value => !string.IsNullOrEmpty(value))];
    }

    private static string DecodeBody(string body, string? transferEncoding)
    {
        if (string.Equals(transferEncoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(body.Replace("\r\n", string.Empty, StringComparison.Ordinal)));
            }
            catch (FormatException)
            {
                return body;
            }
        }

        if (string.Equals(transferEncoding, "quoted-printable", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeQuotedPrintable(body);
        }

        return body;
    }

    private static string DecodeQuotedPrintable(string value)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current != '=')
            {
                bytes.AddRange(Encoding.UTF8.GetBytes([current]));
                continue;
            }

            // "=\r\n" is a soft line break and contributes nothing.
            if (i + 2 < value.Length && value[i + 1] == '\r' && value[i + 2] == '\n')
            {
                i += 2;
                continue;
            }

            if (i + 1 < value.Length && value[i + 1] == '\n')
            {
                i++;
                continue;
            }

            if (i + 2 < value.Length
                && byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var decoded))
            {
                bytes.Add(decoded);
                i += 2;
                continue;
            }

            bytes.Add((byte)current);
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>Decodes RFC 2047 encoded words so Turkish subjects render correctly.</summary>
    private static string DecodeHeader(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return EncodedWordPattern().Replace(value, match =>
        {
            var charset = match.Groups["charset"].Value;
            var encoding = match.Groups["encoding"].Value;
            var payload = match.Groups["payload"].Value;

            try
            {
                var textEncoding = Encoding.GetEncoding(charset);
                if (string.Equals(encoding, "B", StringComparison.OrdinalIgnoreCase))
                {
                    return textEncoding.GetString(Convert.FromBase64String(payload));
                }

                var decoded = DecodeQuotedPrintable(payload.Replace('_', ' '));
                return decoded;
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                return match.Value;
            }
        });
    }

    [GeneratedRegex(@"=\?(?<charset>[^?]+)\?(?<encoding>[BQbq])\?(?<payload>[^?]*)\?=")]
    private static partial Regex EncodedWordPattern();
}
