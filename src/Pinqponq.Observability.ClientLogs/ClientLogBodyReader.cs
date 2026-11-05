using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Pinqponq.Observability.ClientLogs;

internal static class ClientLogBodyReader
{
    public const int StreamBufferSize = 8192;

    public static async Task<TRequest?> Read<TRequest>(
        HttpRequest request,
        int? maxBodyBytes,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            if (maxBodyBytes is not int bodyLimit)
            {
                return await JsonSerializer.DeserializeAsync<TRequest>(request.Body, serializerOptions, cancellationToken);
            }

            var bodyBytes = await ReadLimitedBody(request, bodyLimit, cancellationToken);
            return JsonSerializer.Deserialize<TRequest>(bodyBytes, serializerOptions);
        }
        catch (JsonException exception)
        {
            throw new ClientLogRejectedException(ClientLogRejectionReason.InvalidRequest, "The client log body is not valid JSON.", exception);
        }
    }

    private static async Task<byte[]> ReadLimitedBody(HttpRequest request, int bodyLimit, CancellationToken cancellationToken)
    {
        using var bodyStream = new MemoryStream();
        var buffer = new byte[StreamBufferSize];
        while (bodyStream.Length <= bodyLimit)
        {
            var bytesRead = await request.Body.ReadAsync(buffer, cancellationToken);
            if (bytesRead is 0)
            {
                break;
            }

            bodyStream.Write(buffer, 0, bytesRead);
        }

        if (bodyStream.Length > bodyLimit)
        {
            throw BodyTooLarge(bodyLimit);
        }

        return bodyStream.ToArray();
    }

    private static ClientLogRejectedException BodyTooLarge(int bodyLimit) =>
        new(ClientLogRejectionReason.BodyTooLarge, $"The client log body is longer than {bodyLimit} bytes.");
}
