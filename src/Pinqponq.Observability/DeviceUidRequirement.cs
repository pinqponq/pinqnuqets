using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Pinqponq.Contracts;

namespace Pinqponq.Observability;

/// <summary>
/// The <see cref="ObservabilityOptions.RequireDeviceUid"/> check: a request without a device uid
/// (header or query value) is answered 400 <c>DEVICE_UID_REQUIRED</c> with its own log record.
/// </summary>
internal sealed class DeviceUidRequirement
{
    private const string DeviceUidRequiredResponseCode = "DEVICE_UID_REQUIRED";
    private const string DeviceUidRequiredMessage = "DeviceUid header is required.";

    private static readonly JsonSerializerOptions ErrorResponseSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] DeviceUidValueKeys =
    [
        "DeviceUid",
        "Device-Uid",
        "X-Device-Uid",
        "device_uid"
    ];

    private readonly bool _isRequired;
    private readonly string[] _exemptPathSegments;

    public DeviceUidRequirement(bool isRequired, IEnumerable<string> exemptPathSegments)
    {
        _isRequired = isRequired;
        _exemptPathSegments = exemptPathSegments.ToArray();
    }

    public bool IsViolatedBy(HttpContext httpContext, string requestPath) =>
        _isRequired
        && !ShouldSkipDeviceUidCheck(requestPath)
        && string.IsNullOrWhiteSpace(FindRequestValue(httpContext, DeviceUidValueKeys));

    private bool ShouldSkipDeviceUidCheck(string requestPath)
    {
        return _exemptPathSegments.Any(segment =>
            requestPath.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindRequestValue(HttpContext httpContext, string[] fieldKeys)
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

        return null;
    }

    public static async Task Reject(HttpContext httpContext, ILogService logService, string? identifier, string? correlationId)
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

        httpContext.Response.ContentType = MediaTypeNames.Application.Json;
        httpContext.Response.StatusCode = responseModel.StatusCode;
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(responseModel,
            ErrorResponseSerializerOptions));
    }
}
