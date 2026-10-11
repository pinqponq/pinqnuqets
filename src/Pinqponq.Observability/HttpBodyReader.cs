using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Http;
using Pinqloq;

namespace Pinqponq.Observability;

/// <summary>
/// Reads request and response bodies for the log, up to <see cref="ObservabilityOptions.MaxLoggedBodyBytes"/>,
/// leaving the request body readable for the endpoint.
/// </summary>
internal sealed class HttpBodyReader
{
    private const string MultipartBodyOmitted = "[multipart/form-data body omitted]";
    private const string NonTextBodyOmitted = "[non-text request body omitted]";
    private const string MultipartMediaTypePrefix = "multipart/";
    private const string TextMediaTypePrefix = "text/";
    private const string JsonMediaTypeToken = "json";
    private const string XmlMediaTypeToken = "xml";
    private const string GraphQlMediaType = "application/graphql";
    private const int StreamBufferSize = 8192;

    private readonly int _maxLoggedBodyBytes;

    public HttpBodyReader(int maxLoggedBodyBytes)
    {
        _maxLoggedBodyBytes = maxLoggedBodyBytes;
    }

    public async Task<object?> ReadRequestBody(HttpRequest request, PinqloqRedactionPlan redactionPlan)
    {
        if (!HasRequestBody(request))
        {
            return null;
        }

        // Don't read multipart/form-data requests — binary file data can't be logged, and Kestrel blows up on large sizes
        if (IsMultipartContentType(request.ContentType))
        {
            return MultipartBodyOmitted;
        }

        if (request.ContentLength is > 0 && request.ContentLength > _maxLoggedBodyBytes)
        {
            return $"[request body omitted: body exceeded {_maxLoggedBodyBytes} bytes]";
        }

        if (!IsTextLikeContentType(request.ContentType))
        {
            return NonTextBodyOmitted;
        }

        request.EnableBuffering(bufferLimit: _maxLoggedBodyBytes + 1L);

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        var requestBodyBytes = await ReadStreamPrefix(request.Body, _maxLoggedBodyBytes + 1);
        var isTruncated = requestBodyBytes.Length > _maxLoggedBodyBytes;
        var decodedByteCount = isTruncated ? _maxLoggedBodyBytes : requestBodyBytes.Length;
        var requestBodyText = Encoding.UTF8.GetString(requestBodyBytes, 0, decodedByteCount);

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        if (string.IsNullOrWhiteSpace(requestBodyText))
        {
            return null;
        }

        var formattedBody = LogValueRedactor.FormatBody(request.ContentType, requestBodyText, redactionPlan);
        if (!isTruncated)
        {
            return formattedBody;
        }

        return formattedBody is string formattedText
            ? $"{formattedText}\n[truncated: body exceeded {_maxLoggedBodyBytes} bytes]"
            : new
            {
                body = formattedBody,
                truncated = true,
                maxLoggedBodyBytes = _maxLoggedBodyBytes
            };
    }

    public async Task<object?> ReadResponseBody(HttpResponse response, MemoryStream capturedResponseStream, PinqloqRedactionPlan redactionPlan)
    {
        if (capturedResponseStream.Length is 0)
        {
            return null;
        }

        var responseBodyText = await ReadStreamAsText(capturedResponseStream);
        if (string.IsNullOrEmpty(responseBodyText))
        {
            return null;
        }

        return LogValueRedactor.FormatBody(response.ContentType, responseBodyText, redactionPlan);
    }

    private static bool HasRequestBody(HttpRequest request)
    {
        if (HttpMethods.IsPost(request.Method)
            || HttpMethods.IsPut(request.Method)
            || HttpMethods.IsPatch(request.Method))
        {
            return true;
        }

        return request.ContentLength is > 0;
    }

    private static bool IsMultipartContentType(string? contentType)
    {
        return contentType?.StartsWith(MultipartMediaTypePrefix, StringComparison.OrdinalIgnoreCase) is true;
    }

    private static bool IsTextLikeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith(TextMediaTypePrefix, StringComparison.OrdinalIgnoreCase)
               || contentType.Contains(JsonMediaTypeToken, StringComparison.OrdinalIgnoreCase)
               || contentType.Contains(XmlMediaTypeToken, StringComparison.OrdinalIgnoreCase)
               || contentType.Contains(MediaTypeNames.Application.FormUrlEncoded, StringComparison.OrdinalIgnoreCase)
               || contentType.Contains(GraphQlMediaType, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadStreamPrefix(Stream stream, int maxBytesToRead)
    {
        using var memoryStream = new MemoryStream(capacity: Math.Min(StreamBufferSize, maxBytesToRead));
        var buffer = new byte[Math.Min(StreamBufferSize, maxBytesToRead)];
        var remainingBytes = maxBytesToRead;

        while (remainingBytes > 0)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remainingBytes)));
            if (bytesRead is 0)
            {
                break;
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            remainingBytes -= bytesRead;
        }

        return memoryStream.ToArray();
    }

    private async Task<string> ReadStreamAsText(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var length = stream.CanSeek ? stream.Length : (long?)null;
        if (length.HasValue && length.Value > _maxLoggedBodyBytes)
        {
            var truncatedBuffer = new byte[_maxLoggedBodyBytes];
            var bytesRead = await stream.ReadAsync(truncatedBuffer.AsMemory(0, _maxLoggedBodyBytes));
            var truncatedText = Encoding.UTF8.GetString(truncatedBuffer, 0, bytesRead);
            return $"{truncatedText}\n[truncated: body exceeded {_maxLoggedBodyBytes} bytes]";
        }

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: StreamBufferSize,
            leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
