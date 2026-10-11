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
/// <see cref="ErrorResponse"/>.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private const int ClientClosedRequestStatusCode = 499;
    private const string ClientClosedRequestMessage = "Client closed the request.";

    private readonly RequestDelegate _next;
    private readonly ErrorHandlingOptions _options;
    private readonly ErrorLocalizationOptions _localizationOptions;
    private readonly ErrorMessageCatalog _errorMessageCatalog;
    private readonly IReadOnlyList<IExceptionResolver> _exceptionResolvers;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>Creates the middleware without localization or exception resolvers.</summary>
    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        IOptions<ErrorHandlingOptions> options,
        ILogger<ExceptionHandlingMiddleware> logger)
        : this(
            next,
            options,
            Options.Create(new ErrorLocalizationOptions()),
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
        IEnumerable<IExceptionResolver> exceptionResolvers,
        ILoggerFactory loggerFactory,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _localizationOptions = localizationOptions.Value;
        _errorMessageCatalog = new ErrorMessageCatalog(localizationOptions, loggerFactory.CreateLogger<ErrorMessageCatalog>());
        _exceptionResolvers = exceptionResolvers.ToList();
        _logger = logger;
    }

    /// <summary>Middleware entry point.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
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
            await Handle(context, exception).ConfigureAwait(false);
        }
    }

    private bool IsClientClosedRequest(HttpContext context) =>
        _options.TreatAnyCancellationAsClientClosed || context.RequestAborted.IsCancellationRequested;

    private void HandleClientClosedRequest(HttpContext context, OperationCanceledException exception)
    {
        _logger.LogInformation(exception, "Request cancelled by the client. {Method} {Path}", context.Request.Method, context.Request.Path.Value);

        RequestErrorDetail.Describe(
            context,
            ErrorHandlingEvents.RequestCancelledByClient,
            ClientClosedRequestStatusCode,
            CommonErrorCodes.ApiClientClosedRequest,
            ClientClosedRequestMessage);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ClientClosedRequestStatusCode;
        }
    }

    private async Task Handle(HttpContext context, Exception exception)
    {
        try
        {
            var resolvedError = ResolveError(context, exception);
            var correlationId = ErrorResponseWriter.ResolveCorrelationId(context, _options);

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

            RequestErrorDetail.Describe(
                context,
                ErrorHandlingEvents.BuildExceptionEventName(resolvedError.ReportedException),
                resolvedError.StatusCode,
                resolvedError.ResponseCode,
                resolvedError.Message,
                resolvedError.Language,
                resolvedError.AdditionalDetail);

            context.Response.Clear();
            await ErrorResponseWriter.Write(
                context,
                _options,
                resolvedError.StatusCode,
                resolvedError.ResponseCode,
                resolvedError.Message).ConfigureAwait(false);
        }
        catch (Exception writeException)
        {
            _logger.LogError(writeException, "Failed to write the error response.");
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
            catch (Exception resolverException)
            {
                _logger.LogError(resolverException, "{ExceptionResolver} threw; trying the next resolver.", exceptionResolver.GetType().Name);
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
        catch (Exception resolverException)
        {
            _logger.LogError(resolverException, "StatusMappingResolver threw; falling back to default mapping.");
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

    private sealed record ResolvedError(
        int StatusCode,
        string ResponseCode,
        string Message,
        string? Language,
        Exception ReportedException,
        IReadOnlyDictionary<string, object?>? AdditionalDetail);
}
