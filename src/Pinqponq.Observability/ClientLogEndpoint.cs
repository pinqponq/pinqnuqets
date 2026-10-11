using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pinqloq;

namespace Pinqponq.Observability;

internal static class ClientLogEndpoint
{
    public static IResult Relay(
        ClientLogRequest request,
        HttpContext httpContext,
        IPinqloqLogger pinqloqLogger,
        IOptions<ObservabilityOptions> observabilityOptions)
    {
        var correlationIdHeader = httpContext.Request.Headers[PinqloqRequestLoggingMiddleware.CorrelationIdHeaderName].ToString();
        var correlationId = string.IsNullOrWhiteSpace(correlationIdHeader) ? httpContext.TraceIdentifier : correlationIdHeader;
        var entry = new PinqloqLogEntry
        {
            Event = request.Event,
            DeviceIdentifier = httpContext.Request.Headers[observabilityOptions.Value.DeviceIdentifierHeader].ToString(),
            LogLevel = ParseLogLevel(request.LogLevel),
            LogSourceType = PinqloqLogSourceType.Device,
            CollectionName = observabilityOptions.Value.ClientCollectionName,
            Metadata = request.Metadata,
            Detail = request.Detail,
            Date = request.Date,
            CorrelationId = correlationId,
        };

        pinqloqLogger.Enqueue(entry);

        return Results.NoContent();
    }

    public static PinqloqLogLevel ParseLogLevel(string logLevel)
    {
        return logLevel.ToUpperInvariant() switch
        {
            "DEBUG" => PinqloqLogLevel.Debug,
            "WARNING" or "WARN" => PinqloqLogLevel.Warning,
            "ERROR" => PinqloqLogLevel.Error,
            "FATAL" => PinqloqLogLevel.Fatal,
            _ => PinqloqLogLevel.Information,
        };
    }
}
