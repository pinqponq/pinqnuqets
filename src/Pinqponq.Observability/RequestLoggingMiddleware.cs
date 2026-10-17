using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Pinqloq;
using Pinqponq.Contracts;

namespace Pinqponq.Observability;

/// <summary>
/// Writes one Pinqloq record per request: method, path, status, duration, redacted query and
/// request/response bodies, and the error detail Pinqponq.ErrorHandling put on
/// <see cref="IRequestErrorFeature"/>.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _requestDelegate;
    private readonly string _httpCollectionName;
    private readonly string? _deviceIdentifierHeader;
    private readonly string[] _excludedPathSegments;
    private readonly KeyValuePair<string, string>[] _loggedContextItems;
    private readonly LogValueRedactor _logValueRedactor;
    private readonly HttpBodyReader _bodyReader;

    /// <summary>Creates the middleware.</summary>
    public RequestLoggingMiddleware(
        RequestDelegate requestDelegate,
        IOptions<ObservabilityOptions> observabilityOptions)
    {
        _requestDelegate = requestDelegate;
        _httpCollectionName = observabilityOptions.Value.HttpCollectionName;
        _deviceIdentifierHeader = observabilityOptions.Value.DeviceIdentifierHeader;
        _excludedPathSegments = observabilityOptions.Value.ExcludedPathSegments.ToArray();
        _loggedContextItems = observabilityOptions.Value.LoggedContextItems.ToArray();
        _logValueRedactor = new LogValueRedactor(observabilityOptions.Value.RedactedKeys);
        _bodyReader = new HttpBodyReader(observabilityOptions.Value.MaxLoggedBodyBytes, _logValueRedactor);
    }

    /// <summary>Middleware entry point.</summary>
    public async Task Invoke(
        HttpContext httpContext,
        ILogService logService)
    {
        var requestPath = httpContext.Request.Path.Value ?? string.Empty;
        if (ShouldSkipLogging(requestPath))
        {
            await _requestDelegate(httpContext);
            return;
        }

        var identifier = RequestIdentifiers.ReadDeviceIdentifier(httpContext, _deviceIdentifierHeader);
        var correlationId = RequestIdentifiers.ResolveCorrelationId(httpContext);

        var redactionPlan = PinqloqRedaction.Resolve(httpContext);

        var startedAt = Stopwatch.GetTimestamp();
        var requestBody = await _bodyReader.ReadRequestBody(httpContext.Request, redactionPlan);

        var originalResponseStream = httpContext.Response.Body;
        using var capturedResponseStream = new MemoryStream();
        httpContext.Response.Body = capturedResponseStream;

        try
        {
            await _requestDelegate(httpContext);

            capturedResponseStream.Seek(0, SeekOrigin.Begin);
            await capturedResponseStream.CopyToAsync(originalResponseStream);

            var responseBody = await _bodyReader.ReadResponseBody(httpContext.Response, capturedResponseStream, redactionPlan);
            var exchangeLog = BuildExchangeLog(
                httpContext,
                startedAt,
                requestBody,
                responseBody,
                redactionPlan);

            WriteExchangeLog(
                logService,
                httpContext.Response.StatusCode,
                $"{httpContext.Request.Method} {requestPath}",
                exchangeLog,
                BuildRequestEventName(httpContext, requestPath),
                identifier,
                correlationId);
        }
        catch (Exception caughtException)
        {
            if (capturedResponseStream.Length > 0)
            {
                capturedResponseStream.Seek(0, SeekOrigin.Begin);
                await capturedResponseStream.CopyToAsync(originalResponseStream);
            }

            var exchangeLog = BuildExchangeLog(
                httpContext,
                startedAt,
                requestBody,
                null,
                redactionPlan,
                StatusCodes.Status500InternalServerError);

            logService.Error(
                $"{httpContext.Request.Method} {requestPath}",
                caughtException,
                exchangeLog,
                _httpCollectionName,
                eventName: BuildRequestEventName(httpContext, requestPath),
                identifier: identifier,
                correlationId: correlationId);
            throw;
        }
        finally
        {
            httpContext.Response.Body = originalResponseStream;
        }
    }

    private bool ShouldSkipLogging(string requestPath)
    {
        return _excludedPathSegments.Any(segment =>
            requestPath.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    // Levels the single access record by HTTP status code: 2xx/3xx → Info, 4xx → Warning, 5xx → Error.
    // Pinqponq.ErrorHandling describes a failed request on IRequestErrorFeature, so this single record
    // carries the error's event and detail; no separate error record is produced.
    private void WriteExchangeLog(
        ILogService logService,
        int statusCode,
        string message,
        object exchangeLog,
        string eventName,
        string? identifier,
        string? correlationId)
    {
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logService.Error(message, null, exchangeLog, _httpCollectionName, eventName, identifier, correlationId: correlationId);
        }
        else if (statusCode >= StatusCodes.Status400BadRequest)
        {
            logService.Warn(message, exchangeLog, _httpCollectionName, eventName, identifier, correlationId: correlationId);
        }
        else
        {
            logService.Info(message, exchangeLog, _httpCollectionName, eventName, identifier, correlationId: correlationId);
        }
    }

    // Uses the route pattern (e.g. "api/orders/{id}") so variable segments like id don't
    // produce a different event name per request; falls back to the raw path if the endpoint can't be resolved.
    // Wildcard patterns like a reverse proxy's {**catch-all} route don't carry the real path,
    // so the template is ignored in that case and the real path is used instead (otherwise every request would map to the same event).
    private static string BuildRequestEventName(HttpContext httpContext, string requestPath)
    {
        // A failure described by Pinqponq.ErrorHandling (exception_X, cancel) names the
        // single record of this request; otherwise the name is built from the route.
        var errorEventName = httpContext.Features.Get<IRequestErrorFeature>()?.EventName;
        if (!string.IsNullOrWhiteSpace(errorEventName))
        {
            return errorEventName;
        }

        var routePattern = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern;
        var hasCatchAllSegment = routePattern?.Parameters.Any(parameter => parameter.IsCatchAll) ?? false;
        var routeTemplate = hasCatchAllSegment ? null : routePattern?.RawText;
        var normalizedRoute = NormalizeRouteSegment(routeTemplate ?? requestPath);
        var methodSegment = httpContext.Request.Method.ToLowerInvariant();
        return $"{methodSegment}_{normalizedRoute}";
    }

    private static string NormalizeRouteSegment(string routeSegment)
    {
        var sanitized = routeSegment
            .Trim('/')
            .Replace('/', '_')
            .Replace('-', '_')
            .Replace("{", string.Empty)
            .Replace("}", string.Empty)
            .ToLowerInvariant();

        return string.IsNullOrEmpty(sanitized) ? "root" : sanitized;
    }

    private object BuildExchangeLog(
        HttpContext httpContext,
        long startedAt,
        object? requestBody,
        object? responseBody,
        PinqloqRedactionPlan redactionPlan,
        int? fallbackStatusCode = null)
    {
        var request = httpContext.Request;
        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var statusCode = httpContext.Response.HasStarted
            ? httpContext.Response.StatusCode
            : fallbackStatusCode ?? httpContext.Response.StatusCode;

        var exchangeLog = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["RequestMethod"] = request.Method,
            ["path"] = request.Path.Value,
            ["ResponseCode"] = statusCode,
            ["elapsedMs"] = Math.Round(elapsedMilliseconds, 2)
        };

        if (request.Query.Count > 0)
        {
            exchangeLog["query"] = _logValueRedactor.SanitizeQuery(request.Query, redactionPlan);
        }

        if (requestBody is not null)
        {
            exchangeLog["InputJson"] = requestBody;
        }

        if (responseBody is not null)
        {
            exchangeLog["OutputJson"] = responseBody;
        }

        foreach (var (contextItemKey, recordField) in _loggedContextItems)
        {
            var contextItemValue = GetContextItem(httpContext, contextItemKey);
            if (!string.IsNullOrEmpty(contextItemValue))
            {
                exchangeLog[recordField] = contextItemValue;
            }
        }

        var errorDetail = httpContext.Features.Get<IRequestErrorFeature>()?.Detail;
        if (errorDetail is not null)
        {
            exchangeLog["exception"] = errorDetail;
        }

        return exchangeLog;
    }

    private static string? GetContextItem(HttpContext httpContext, string contextItemKey)
    {
        return httpContext.Items.TryGetValue(contextItemKey, out var contextValue)
            ? contextValue?.ToString()
            : null;
    }
}
