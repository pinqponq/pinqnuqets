using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pinqponq.Contracts;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Catches unhandled exceptions, emits a structured (Pinqloq-compatible) log record,
/// describes the failure on <see cref="IRequestErrorFeature"/> and writes a standard
/// <see cref="ErrorResponse"/>. Also answers every request 503 while maintenance mode is on.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private const int ClientClosedRequestStatusCode = 499;
    private const string ClientClosedRequestMessage = "Client closed the request.";
    private const string DefaultMaintenanceMessage = "System is under maintenance";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;
    private readonly ErrorHandlingOptions _options;
    private readonly ErrorLocalizationOptions _localizationOptions;
    private readonly ErrorMessageCatalog _errorMessageCatalog;
    private readonly IOptionsMonitor<MaintenanceModeOptions>? _maintenanceModeOptions;
    private readonly IReadOnlyList<IExceptionResolver> _exceptionResolvers;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>Creates the middleware without localization, maintenance mode or exception resolvers.</summary>
    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        IOptions<ErrorHandlingOptions> options,
        ILogger<ExceptionHandlingMiddleware> logger)
        : this(
            next,
            options,
            Options.Create(new ErrorLocalizationOptions()),
            maintenanceModeOptions: null,
            exceptionResolvers: [],
            NullLoggerFactory.Instance,
            logger)
    {
    }

    /// <summary>Creates the middleware.</summary>
    [ActivatorUtilitiesConstructor]
    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        IOptions<ErrorHandlingOptions> options,
        IOptions<ErrorLocalizationOptions> localizationOptions,
        IOptionsMonitor<MaintenanceModeOptions>? maintenanceModeOptions,
        IEnumerable<IExceptionResolver> exceptionResolvers,
        ILoggerFactory loggerFactory,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _localizationOptions = localizationOptions.Value;
        _errorMessageCatalog = new ErrorMessageCatalog(localizationOptions, loggerFactory.CreateLogger<ErrorMessageCatalog>());
        _maintenanceModeOptions = maintenanceModeOptions;
        _exceptionResolvers = exceptionResolvers.ToList();
        _logger = logger;
    }

    /// <summary>Middleware entry point.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (_maintenanceModeOptions?.CurrentValue is { Enabled: true } maintenanceMode)
        {
            await WriteMaintenanceResponseAsync(context, maintenanceMode).ConfigureAwait(false);
            return;
        }

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsClientClosedRequest(context))
        {
            HandleClientClosedRequest(context, exception);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception).ConfigureAwait(false);
        }
    }

    private bool IsClientClosedRequest(HttpContext context) =>
        _options.TreatAnyCancellationAsClientClosed || context.RequestAborted.IsCancellationRequested;

    private void HandleClientClosedRequest(HttpContext context, OperationCanceledException exception)
    {
        _logger.LogInformation(exception, "Request cancelled by the client. {Method} {Path}", context.Request.Method, context.Request.Path.Value);

        var detail = BuildErrorDetail(
            context,
            ClientClosedRequestStatusCode,
            CommonErrorCodes.ApiClientClosedRequest,
            ClientClosedRequestMessage,
            language: null,
            additionalDetail: null);
        context.Features.Set<IRequestErrorFeature>(new RequestErrorFeature(ErrorHandlingEvents.RequestCancelledByClient, detail));

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ClientClosedRequestStatusCode;
        }
    }

    private async Task WriteMaintenanceResponseAsync(HttpContext context, MaintenanceModeOptions maintenanceMode)
    {
        var message = maintenanceMode.Message ?? DefaultMaintenanceMessage;
        _logger.LogWarning("Request blocked by maintenance mode. {Method} {Path}", context.Request.Method, context.Request.Path.Value);

        var detail = BuildErrorDetail(
            context,
            StatusCodes.Status503ServiceUnavailable,
            CommonErrorCodes.SystemMaintenance,
            message,
            language: null,
            additionalDetail: null);
        context.Features.Set<IRequestErrorFeature>(new RequestErrorFeature(ErrorHandlingEvents.RequestBlockedMaintenanceMode, detail));

        await WriteErrorResponseAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            CommonErrorCodes.SystemMaintenance,
            message).ConfigureAwait(false);
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        try
        {
            var resolvedError = ResolveError(context, exception);
            var correlationId = ResolveCorrelationId(context);

            var isServerError = resolvedError.StatusCode >= StatusCodes.Status500InternalServerError;
            _logger.Log(
                isServerError ? LogLevel.Error : LogLevel.Warning,
                exception,
                "Request failed. {ResponseCode} {StatusCode} {Method} {Path} traceId={TraceId} correlationId={CorrelationId}",
                resolvedError.ResponseCode,
                resolvedError.StatusCode,
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier,
                correlationId);

            if (context.Response.HasStarted)
            {
                return;
            }

            var detail = BuildErrorDetail(
                context,
                resolvedError.StatusCode,
                resolvedError.ResponseCode,
                resolvedError.Message,
                resolvedError.Language,
                resolvedError.AdditionalDetail);
            var eventName = ErrorHandlingEvents.BuildExceptionEventName(resolvedError.ReportedException);
            context.Features.Set<IRequestErrorFeature>(new RequestErrorFeature(eventName, detail));

            context.Response.Clear();
            await WriteErrorResponseAsync(
                context,
                resolvedError.StatusCode,
                resolvedError.ResponseCode,
                resolvedError.Message).ConfigureAwait(false);
        }
        catch (Exception writeEx)
        {
            _logger.LogError(writeEx, "Failed to write the error response.");
        }
    }

    private ResolvedError ResolveError(HttpContext context, Exception exception)
    {
        var resolution = ResolveWithExceptionResolvers(exception);
        var (statusCode, mappedResponseCode) = resolution is null
            ? ResolveStatusMapping(exception)
            : (resolution.StatusCode, resolution.ResponseCode);
        var reportedException = resolution?.ReportedException ?? exception;

        if (!_localizationOptions.Enabled)
        {
            var message = _options.IncludeExceptionMessage
                ? exception.Message
                : ExceptionMapping.DefaultMessage(statusCode);
            return new ResolvedError(statusCode, mappedResponseCode, message, Language: null, reportedException, resolution?.LogDetail);
        }

        // Localized mode follows the convention of throwing with the response code as the
        // message (throw new InvalidOperationException("user_not_found")); only an
        // IExceptionResolver names the code explicitly.
        var requestedResponseCode = resolution is null ? exception.Message : resolution.ResponseCode;
        var fallbackResponseCode = resolution?.FallbackResponseCode ?? _localizationOptions.FallbackResponseCode;
        var responseCode = _errorMessageCatalog.Contains(requestedResponseCode)
            ? requestedResponseCode
            : fallbackResponseCode;
        var language = ResolveLanguage(context);
        var localizedMessage = _errorMessageCatalog.FindMessage(responseCode, language) ?? _localizationOptions.DefaultMessage;
        return new ResolvedError(statusCode, responseCode, localizedMessage, language, reportedException, resolution?.LogDetail);
    }

    private ExceptionResolution? ResolveWithExceptionResolvers(Exception exception)
    {
        foreach (var exceptionResolver in _exceptionResolvers)
        {
            try
            {
                var resolution = exceptionResolver.Resolve(exception);
                if (resolution is not null)
                {
                    return resolution;
                }
            }
            catch (Exception resolverEx)
            {
                _logger.LogError(resolverEx, "{ExceptionResolver} threw; trying the next resolver.", exceptionResolver.GetType().Name);
            }
        }

        return null;
    }

    private (int StatusCode, string ResponseCode) ResolveStatusMapping(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return (StatusCodes.Status504GatewayTimeout, "timeout");
        }

        try
        {
            var overridden = _options.StatusMappingResolver?.Invoke(exception);
            if (overridden is ExceptionStatusMapping mapping)
            {
                return (mapping.StatusCode, mapping.ResponseCode);
            }
        }
        catch (Exception resolverEx)
        {
            _logger.LogError(resolverEx, "StatusMappingResolver threw; falling back to default mapping.");
        }

        return ExceptionMapping.Map(exception);
    }

    private string ResolveLanguage(HttpContext context)
    {
        var language = context.Request.Headers[_localizationOptions.LanguageHeader].ToString();
        return string.IsNullOrWhiteSpace(language)
            ? ErrorMessageCatalog.NormalizeLanguage(_localizationOptions.DefaultLanguage)
            : ErrorMessageCatalog.NormalizeLanguage(language);
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_options.CorrelationIdHeader, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.ToString();
        }

        return context.TraceIdentifier;
    }

    private static Dictionary<string, object?> BuildErrorDetail(
        HttpContext context,
        int statusCode,
        string responseCode,
        string message,
        string? language,
        IReadOnlyDictionary<string, object?>? additionalDetail)
    {
        var request = context.Request;
        var detail = new Dictionary<string, object?>
        {
            ["traceId"] = context.TraceIdentifier,
            ["method"] = request.Method,
            ["statusCode"] = statusCode,
            ["errorCode"] = responseCode,
            ["errorMessage"] = message,
        };

        if (!string.IsNullOrWhiteSpace(language))
        {
            detail["language"] = language;
        }

        if (!string.IsNullOrWhiteSpace(request.QueryString.Value))
        {
            detail["queryString"] = request.QueryString.Value;
        }

        if (additionalDetail is not null)
        {
            foreach (var (key, value) in additionalDetail)
            {
                detail[key] = value;
            }
        }

        return detail;
    }

    private async Task WriteErrorResponseAsync(HttpContext context, int statusCode, string responseCode, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var traceId = _options.IncludeTraceId ? ResolveCorrelationId(context) : null;
        var body = new ErrorResponse
        {
            Status = false,
            StatusCode = statusCode,
            ResponseCode = responseCode,
            Message = message,
            TraceId = traceId,
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions), CancellationToken.None)
            .ConfigureAwait(false);
    }

    private sealed record ResolvedError(
        int StatusCode,
        string ResponseCode,
        string Message,
        string? Language,
        Exception ReportedException,
        IReadOnlyDictionary<string, object?>? AdditionalDetail);
}
