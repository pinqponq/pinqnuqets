using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqloq;

namespace Pinqponq.Observability.ClientLogs;

/// <summary>
/// Checks a client log request as a whole, then writes each entry to Pinqloq as a device log.
/// A refused request writes nothing.
/// </summary>
internal sealed class ClientLogRelay
{
    private const string RedactedValue = "***REDACTED***";

    private readonly IPinqloqLogger _pinqloqLogger;
    private readonly ClientLogOptions _clientLogOptions;
    private readonly ObservabilityOptions _observabilityOptions;
    private readonly JsonSerializerOptions _serializerOptions;

    public ClientLogRelay(
        IPinqloqLogger pinqloqLogger,
        IOptions<ClientLogOptions> clientLogOptions,
        IOptions<ObservabilityOptions> observabilityOptions,
        IOptions<JsonOptions> jsonOptions)
    {
        _pinqloqLogger = pinqloqLogger;
        _clientLogOptions = clientLogOptions.Value;
        _observabilityOptions = observabilityOptions.Value;
        _serializerOptions = jsonOptions.Value.SerializerOptions;
    }

    public async Task RelayEntry(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var entryRequest = await ClientLogBodyReader.Read<ClientLogEntryRequest>(
            httpContext.Request, _clientLogOptions.MaxBodyBytes, _serializerOptions, cancellationToken);
        Relay(httpContext, [entryRequest]);
    }

    public async Task RelayBatch(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var batchRequest = await ClientLogBodyReader.Read<ClientLogBatchRequest>(
            httpContext.Request, _clientLogOptions.MaxBodyBytes, _serializerOptions, cancellationToken);
        if (batchRequest?.Logs is not { Count: > 0 } entryRequests)
        {
            throw new ClientLogRejectedException(ClientLogRejectionReason.InvalidRequest, "The client log batch has no entries.");
        }

        if (entryRequests.Count > _clientLogOptions.MaxEntriesPerBatch)
        {
            throw new ClientLogRejectedException(
                ClientLogRejectionReason.TooManyEntries,
                $"The client log batch has more than {_clientLogOptions.MaxEntriesPerBatch} entries.");
        }

        Relay(httpContext, entryRequests);
    }

    private void Relay(HttpContext httpContext, IReadOnlyList<ClientLogEntryRequest?> entryRequests)
    {
        var validEntryRequests = entryRequests.Select(ValidateEntry).ToList();

        var deviceIdentifier = CutToMaxValueLength(ReadDeviceIdentifier(httpContext));
        var correlationId = ResolveCorrelationId(httpContext);
        var entries = validEntryRequests
            .Select(entryRequest => BuildEntry(entryRequest, deviceIdentifier, correlationId))
            .ToList();

        var enrichers = httpContext.RequestServices.GetServices<IClientLogEnricher>().ToList();
        foreach (var entry in entries)
        {
            foreach (var enricher in enrichers)
            {
                enricher.Enrich(httpContext, entry);
            }
        }

        foreach (var entry in entries)
        {
            _pinqloqLogger.Enqueue(entry);
        }
    }

    private ClientLogEntryRequest ValidateEntry(ClientLogEntryRequest? entryRequest)
    {
        if (entryRequest is null || string.IsNullOrWhiteSpace(entryRequest.Event))
        {
            throw new ClientLogRejectedException(ClientLogRejectionReason.InvalidRequest, "A client log entry has no event name.");
        }

        if (entryRequest.Event.Length > _clientLogOptions.MaxValueLength)
        {
            throw new ClientLogRejectedException(
                ClientLogRejectionReason.EventTooLong,
                $"A client log event name is longer than {_clientLogOptions.MaxValueLength} characters.");
        }

        var fieldCount = (entryRequest.Metadata?.Count ?? 0) + (entryRequest.Detail?.Count ?? 0);
        if (fieldCount > _clientLogOptions.MaxFieldsPerEntry)
        {
            throw new ClientLogRejectedException(
                ClientLogRejectionReason.TooManyFields,
                $"A client log entry has more than {_clientLogOptions.MaxFieldsPerEntry} fields.");
        }

        return entryRequest;
    }

    private PinqloqLogEntry BuildEntry(ClientLogEntryRequest entryRequest, string deviceIdentifier, string correlationId)
    {
        var logLevel = ClientLogLevels.Parse(entryRequest.LogLevel);
        var metadata = SanitizeFields(entryRequest.Metadata);
        var detail = SanitizeFields(entryRequest.Detail);
        return new PinqloqLogEntry
        {
            Event = entryRequest.Event!,
            DeviceIdentifier = deviceIdentifier,
            LogLevel = logLevel,
            LogSourceType = PinqloqLogSourceType.Device,
            CollectionName = _clientLogOptions.CollectionName,
            Metadata = metadata,
            Detail = detail,
            Date = entryRequest.Date,
            CorrelationId = correlationId
        };
    }

    private Dictionary<string, string>? SanitizeFields(Dictionary<string, string?>? fields)
    {
        return fields?.ToDictionary(
            field => field.Key,
            field => IsRedactedKey(field.Key) ? RedactedValue : CutToMaxValueLength(field.Value ?? string.Empty));
    }

    private bool IsRedactedKey(string fieldKey)
    {
        return _observabilityOptions.RedactedKeys.Contains(fieldKey, StringComparer.OrdinalIgnoreCase)
               || _clientLogOptions.RedactedKeyParts.Any(keyPart => fieldKey.Contains(keyPart, StringComparison.OrdinalIgnoreCase));
    }

    private string CutToMaxValueLength(string value)
    {
        return _clientLogOptions.MaxValueLength is int maxValueLength && value.Length > maxValueLength
            ? value[..maxValueLength]
            : value;
    }

    private string ReadDeviceIdentifier(HttpContext httpContext)
    {
        var deviceIdentifierHeader = _observabilityOptions.DeviceIdentifierHeader;
        return string.IsNullOrWhiteSpace(deviceIdentifierHeader)
            ? string.Empty
            : httpContext.Request.Headers[deviceIdentifierHeader].ToString();
    }

    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        var correlationIdHeader = httpContext.Request.Headers[PinqloqRequestLoggingMiddleware.CorrelationIdHeaderName].ToString();
        return string.IsNullOrWhiteSpace(correlationIdHeader) ? httpContext.TraceIdentifier : correlationIdHeader;
    }
}
