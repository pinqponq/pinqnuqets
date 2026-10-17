using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pinqloq;

namespace Pinqponq.Observability;

internal static class ClientLogEndpoint
{
    public const string PathSegment = "/client-logs";
    public const string DefaultRoute = "/api" + PathSegment;

    private const string DebugLevel = "DEBUG";
    private const string WarningLevel = "WARNING";
    private const string WarnLevel = "WARN";
    private const string ErrorLevel = "ERROR";
    private const string FatalLevel = "FATAL";

    public static IResult Relay(
        ClientLogRequest request,
        HttpContext httpContext,
        IPinqloqLogger pinqloqLogger,
        IOptions<ObservabilityOptions> observabilityOptions)
    {
        var entry = new PinqloqLogEntry
        {
            Event = request.Event,
            DeviceIdentifier = RequestIdentifiers.ReadDeviceIdentifier(httpContext, observabilityOptions.Value.DeviceIdentifierHeader),
            LogLevel = ParseLogLevel(request.LogLevel),
            LogSourceType = PinqloqLogSourceType.Device,
            CollectionName = observabilityOptions.Value.ClientCollectionName,
            Metadata = request.Metadata,
            Detail = request.Detail,
            Date = request.Date,
            CorrelationId = RequestIdentifiers.ResolveCorrelationId(httpContext),
        };

        pinqloqLogger.Enqueue(entry);

        return Results.NoContent();
    }

    public static PinqloqLogLevel ParseLogLevel(string? logLevel)
    {
        return logLevel?.ToUpperInvariant() switch
        {
            DebugLevel => PinqloqLogLevel.Debug,
            WarningLevel or WarnLevel => PinqloqLogLevel.Warning,
            ErrorLevel => PinqloqLogLevel.Error,
            FatalLevel => PinqloqLogLevel.Fatal,
            _ => PinqloqLogLevel.Information,
        };
    }
}
