using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Writes the <see cref="ErrorResponse"/> body and resolves the request's correlation id.
/// </summary>
internal static class ErrorResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ResolveCorrelationId(HttpContext context, ErrorHandlingOptions options)
    {
        if (context.Request.Headers.TryGetValue(options.CorrelationIdHeader, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.ToString();
        }

        return context.TraceIdentifier;
    }

    public static async Task Write(HttpContext context, ErrorHandlingOptions options, int statusCode, string responseCode, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var body = new ErrorResponse
        {
            Status = false,
            StatusCode = statusCode,
            ResponseCode = responseCode,
            Message = message,
            TraceId = ResolveCorrelationId(context, options),
        };

        context.Response.ContentType = MediaTypeNames.Application.Json;
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions), CancellationToken.None)
            .ConfigureAwait(false);
    }
}
