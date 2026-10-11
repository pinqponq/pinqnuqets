using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Pinqloq;
using Pinqponq.Contracts;

namespace Pinqponq.Observability;

/// <summary>
/// Writes one Pinqloq record per request: method, path, status, duration, redacted query and
/// request/response bodies, and the error detail Pinqponq.ErrorHandling put on
/// <see cref="IRequestErrorFeature"/>. Optionally rejects requests without a device uid.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private const string RedactedValue = "***REDACTED***";
    private const string RedactedUrl = "****";
    private const string OpenApiBodyOmitted = "[openapi document omitted]";
    private const string MultipartBodyOmitted = "[multipart/form-data body omitted]";
    private const string NonTextBodyOmitted = "[non-text request body omitted]";
    private const string ProjectIdKey = "ProjectId";
    private const string ProjectKeyKey = "ProjectKey";
    private const string KeyIdKey = "KeyId";
    private const string DeviceUidRequiredResponseCode = "DEVICE_UID_REQUIRED";
    private const string DeviceUidRequiredMessage = "DeviceUid header is required.";

    private static readonly JsonSerializerOptions ErrorResponseSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] AppVersionValueKeys =
    [
        "AppVersionName",
        "App-Version-Name",
        "X-App-Version-Name",
        "AppVersion",
        "App-Version",
        "X-App-Version",
        "Version"
    ];

    private static readonly string[] DeviceUidValueKeys =
    [
        "DeviceUid",
        "DeviceUID",
        "Device-Uid",
        "X-Device-Uid",
        "device_uid"
    ];

    private readonly HashSet<string> _sensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "SecretKey",
        "X-Internal-Key",
        "ApiKey",
        "X-Api-Key",
        "AccessToken",
        "RefreshToken",
        "Password"
    };

    private readonly RequestDelegate _requestDelegate;
    private readonly int _maxLoggedBodyBytes;
    private readonly bool _requireDeviceUid;
    private readonly string _httpCollectionName;
    private readonly string _deviceIdentifierHeader;
    private readonly string[] _excludedPathSegments;
    private readonly string[] _deviceUidExemptPathSegments;

    /// <summary>Creates the middleware.</summary>
    public RequestLoggingMiddleware(
        RequestDelegate requestDelegate,
        IOptions<ObservabilityOptions> observabilityOptions)
    {
        _requestDelegate = requestDelegate;
        _maxLoggedBodyBytes = observabilityOptions.Value.MaxLoggedBodyBytes;
        _requireDeviceUid = observabilityOptions.Value.RequireDeviceUid;
        _httpCollectionName = observabilityOptions.Value.HttpCollectionName;
        _deviceIdentifierHeader = observabilityOptions.Value.DeviceIdentifierHeader;
        _excludedPathSegments = observabilityOptions.Value.ExcludedPathSegments.ToArray();
        _deviceUidExemptPathSegments = observabilityOptions.Value.DeviceUidExemptPathSegments.ToArray();
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

        var identifier = httpContext.Request.Headers[_deviceIdentifierHeader].ToString();
        var correlationId = ResolveCorrelationId(httpContext);

        if (_requireDeviceUid
            && !ShouldSkipDeviceUidCheck(requestPath)
            && string.IsNullOrWhiteSpace(FindRequestValue(httpContext, requestBody: null, DeviceUidValueKeys)))
        {
            await WriteDeviceUidRequiredResponse(httpContext, logService, identifier, correlationId);
            return;
        }

        var redactionPlan = PinqloqRedaction.Resolve(httpContext);

        var startedAt = Stopwatch.GetTimestamp();
        var requestBody = await ReadRequestBody(httpContext.Request, redactionPlan);

        var originalResponseStream = httpContext.Response.Body;
        using var capturedResponseStream = new MemoryStream();
        httpContext.Response.Body = capturedResponseStream;

        try
        {
            await _requestDelegate(httpContext);

            capturedResponseStream.Seek(0, SeekOrigin.Begin);
            await capturedResponseStream.CopyToAsync(originalResponseStream);

            var responseBody = await ReadResponseBody(httpContext.Response, capturedResponseStream, redactionPlan);
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

    private bool ShouldSkipDeviceUidCheck(string requestPath)
    {
        return _deviceUidExemptPathSegments.Any(segment =>
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

    // Mirrors the SDK's own resolution order (see PinqloqRequestLoggingMiddleware.ResolveCorrelationId) so a
    // caller-propagated correlation id survives through this custom middleware instead of only the trace id.
    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers[PinqloqRequestLoggingMiddleware.CorrelationIdHeaderName].ToString();
        return string.IsNullOrWhiteSpace(header) ? httpContext.TraceIdentifier : header;
    }

    // Uses the route pattern (e.g. "api/tickets/{id}/convert") so variable segments like id don't
    // produce a different event name per request; falls back to the raw path if the endpoint can't be resolved.
    // Wildcard patterns like the Gateway's {**catch-all} proxy route don't carry the real path,
    // so the template is ignored in that case and the real path is used instead (otherwise every request would map to the same event).
    private static string BuildRequestEventName(HttpContext httpContext, string requestPath)
    {
        // A failure described by Pinqponq.ErrorHandling (exception_X, maintenance, cancel) names the
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

    private static async Task WriteDeviceUidRequiredResponse(HttpContext httpContext, ILogService logService, string? identifier, string? correlationId)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        logService.Warn(
            "Request rejected because DeviceUid header is missing.",
            new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["method"] = httpContext.Request.Method,
                ["path"] = httpContext.Request.Path.Value,
                ["responseCode"] = DeviceUidRequiredResponseCode
            },
            eventName: ObservabilityEvents.RequestRejectedDeviceUidMissing,
            identifier: identifier,
            correlationId: correlationId);

        var responseModel = new BaseResponseModel
        {
            Status = false,
            ResponseCode = DeviceUidRequiredResponseCode,
            Message = DeviceUidRequiredMessage,
            StatusCode = StatusCodes.Status400BadRequest
        };

        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = responseModel.StatusCode;
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(responseModel,
            ErrorResponseSerializerOptions));
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
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var statusCode = httpContext.Response.HasStarted
            ? httpContext.Response.StatusCode
            : fallbackStatusCode ?? httpContext.Response.StatusCode;

        var exchangeLog = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["RequestMethod"] = request.Method,
            ["path"] = request.Path.Value,
            ["ResponseCode"] = statusCode,
            ["elapsedMs"] = Math.Round(elapsedMs, 2)
        };

        if (request.Query.Count > 0)
        {
            exchangeLog["query"] = SanitizeQuery(request.Query, redactionPlan);
        }

        if (requestBody is not null)
        {
            exchangeLog["InputJson"] = requestBody;
        }

        if (responseBody is not null)
        {
            exchangeLog["OutputJson"] = responseBody;
        }

        var projectId = GetContextItem(httpContext, ProjectIdKey);
        if (!string.IsNullOrEmpty(projectId))
        {
            exchangeLog["projectId"] = projectId;
        }

        var projectKey = GetContextItem(httpContext, ProjectKeyKey);
        if (!string.IsNullOrEmpty(projectKey))
        {
            exchangeLog["projectKey"] = projectKey;
        }

        var keyId = GetContextItem(httpContext, KeyIdKey);
        if (!string.IsNullOrEmpty(keyId))
        {
            exchangeLog["keyId"] = keyId;
        }

        var errorDetail = httpContext.Features.Get<IRequestErrorFeature>()?.Detail;
        if (errorDetail is not null)
        {
            exchangeLog["exception"] = errorDetail;
        }

        return exchangeLog;
    }

    private string? FindRequestValue(HttpContext httpContext, object? requestBody, string[] fieldKeys)
    {
        foreach (var fieldKey in fieldKeys)
        {
            var headerValue = httpContext.Request.Headers[fieldKey].ToString();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue;
            }

            var queryValue = httpContext.Request.Query[fieldKey].ToString();
            if (!string.IsNullOrWhiteSpace(queryValue))
            {
                return queryValue;
            }
        }

        return TryFindValue(requestBody, fieldKeys, out var requestBodyValue)
            ? requestBodyValue
            : null;
    }

    private static bool TryFindValue(object? source, string[] fieldKeys, out string? value)
    {
        value = null;

        if (source is IReadOnlyDictionary<string, object?> dictionary)
        {
            foreach (var item in dictionary)
            {
                if (fieldKeys.Any(fieldKey =>
                        string.Equals(fieldKey, item.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    value = ConvertLogScalarToString(item.Value);
                    return !string.IsNullOrWhiteSpace(value);
                }
            }

            foreach (var item in dictionary.Values)
            {
                if (TryFindValue(item, fieldKeys, out value))
                {
                    return true;
                }
            }
        }

        if (source is IEnumerable<object?> values && source is not string)
        {
            foreach (var item in values)
            {
                if (TryFindValue(item, fieldKeys, out value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ConvertLogScalarToString(object? value)
    {
        return value switch
        {
            null => null,
            string stringValue => stringValue,
            JsonElement jsonElement => jsonElement.ValueKind is JsonValueKind.String
                ? jsonElement.GetString()
                : jsonElement.ToString(),
            _ => value.ToString()
        };
    }

    private async Task<object?> ReadResponseBody(HttpResponse response, MemoryStream capturedResponseStream, PinqloqRedactionPlan redactionPlan)
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

        return FormatBody(response.ContentType, responseBodyText, redactionPlan);
    }

    private async Task<object?> ReadRequestBody(HttpRequest request, PinqloqRedactionPlan redactionPlan)
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

        var formattedBody = FormatBody(request.ContentType, requestBodyText, redactionPlan);
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
        return contentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) is true;
    }

    private static bool IsTextLikeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("application/graphql", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadStreamPrefix(Stream stream, int maxBytesToRead)
    {
        using var memoryStream = new MemoryStream(capacity: maxBytesToRead);
        var buffer = new byte[Math.Min(8192, maxBytesToRead)];
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
        var maxLoggedBodyBytes = _maxLoggedBodyBytes;
        if (length.HasValue && length.Value > maxLoggedBodyBytes)
        {
            var truncatedBuffer = new byte[maxLoggedBodyBytes];
            var bytesRead = await stream.ReadAsync(truncatedBuffer.AsMemory(0, maxLoggedBodyBytes));
            var truncatedText = Encoding.UTF8.GetString(truncatedBuffer, 0, bytesRead);
            return $"{truncatedText}\n[truncated: body exceeded {_maxLoggedBodyBytes} bytes]";
        }

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 8192,
            leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    private object? FormatBody(string? contentType, string bodyText, PinqloqRedactionPlan redactionPlan)
    {
        if (IsOpenApiDocument(bodyText))
        {
            return OpenApiBodyOmitted;
        }

        if (TryParseJsonBody(bodyText, redactionPlan, out var jsonBody))
        {
            return jsonBody;
        }

        if (contentType?.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) is true)
        {
            return QueryHelpers.ParseQuery(bodyText).ToDictionary(
                formParameter => formParameter.Key,
                formParameter => SanitizeValue(formParameter.Key, formParameter.Value.ToString(), redactionPlan));
        }

        return bodyText;
    }

    private static bool IsOpenApiDocument(string bodyText)
    {
        var trimmedBody = bodyText.TrimStart();
        if (!trimmedBody.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var parsedJsonDocument = JsonDocument.Parse(bodyText);
            var root = parsedJsonDocument.RootElement;
            return root.TryGetProperty("openapi", out _)
                   && root.TryGetProperty("paths", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryParseJsonBody(string bodyText, PinqloqRedactionPlan redactionPlan, out object? jsonBody)
    {
        jsonBody = null;
        var trimmedBody = bodyText.TrimStart();
        if (!trimmedBody.StartsWith("{", StringComparison.Ordinal) &&
            !trimmedBody.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var parsedJsonDocument = JsonDocument.Parse(bodyText);
            if (parsedJsonDocument.RootElement.ValueKind is JsonValueKind.Object
                && parsedJsonDocument.RootElement.TryGetProperty("openapi", out _)
                && parsedJsonDocument.RootElement.TryGetProperty("paths", out _))
            {
                jsonBody = OpenApiBodyOmitted;
                return true;
            }

            jsonBody = SanitizeJsonElement(parsedJsonDocument.RootElement, redactionPlan);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private object? SanitizeJsonElement(JsonElement element, PinqloqRedactionPlan redactionPlan)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                jsonProperty => jsonProperty.Name,
                jsonProperty => IsSensitiveKey(jsonProperty.Name, redactionPlan)
                    ? RedactedValue
                    : SanitizeJsonElement(jsonProperty.Value, redactionPlan)),
            JsonValueKind.Array => element.EnumerateArray().Select(arrayElement => SanitizeJsonElement(arrayElement, redactionPlan)).ToList(),
            JsonValueKind.String => SanitizeStringValue(element.GetString()),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private object? SanitizeStringValue(string? stringToSanitize)
    {
        if (IsUrlValue(stringToSanitize))
        {
            return RedactedUrl;
        }

        return stringToSanitize;
    }

    private static bool IsUrlValue(string? stringToCheck)
    {
        if (string.IsNullOrEmpty(stringToCheck))
        {
            return false;
        }

        return stringToCheck.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || stringToCheck.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, object?> SanitizeQuery(IQueryCollection query, PinqloqRedactionPlan redactionPlan)
    {
        return query.ToDictionary(
            queryParameter => queryParameter.Key,
            queryParameter => SanitizeValue(queryParameter.Key, queryParameter.Value.ToString(), redactionPlan));
    }

    private object? SanitizeValue(string fieldKey, string? queryParameterValue, PinqloqRedactionPlan redactionPlan)
    {
        if (IsSensitiveKey(fieldKey, redactionPlan))
        {
            return RedactedValue;
        }

        if (IsUrlValue(queryParameterValue))
        {
            return RedactedUrl;
        }

        return queryParameterValue;
    }

    private bool IsSensitiveKey(string fieldKey, PinqloqRedactionPlan redactionPlan)
    {
        if (_sensitiveKeys.Contains(fieldKey))
        {
            return true;
        }

        if (fieldKey.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || fieldKey.Contains("token", StringComparison.OrdinalIgnoreCase)
            || fieldKey.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return redactionPlan.ShouldRedact(fieldKey);
    }

    private static string? GetContextItem(HttpContext httpContext, string contextItemKey)
    {
        return httpContext.Items.TryGetValue(contextItemKey, out var contextValue)
            ? contextValue?.ToString()
            : null;
    }
}
