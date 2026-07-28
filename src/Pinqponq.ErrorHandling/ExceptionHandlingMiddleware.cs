using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Catches unhandled exceptions, emits a structured (Pinqloq-compatible) log record and
/// writes a standard <see cref="ErrorResponse"/>.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private const int ClientClosedRequestStatusCode = 499;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;
    private readonly ErrorHandlingOptions _options;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        IOptions<ErrorHandlingOptions> options,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Middleware entry point.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ClientClosedRequestStatusCode;
            }
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        try
        {
            var (statusCode, responseCode) = Resolve(exception);
            var correlationId = ResolveCorrelationId(context);

            var isServerError = statusCode >= StatusCodes.Status500InternalServerError;
            _logger.Log(
                isServerError ? LogLevel.Error : LogLevel.Warning,
                exception,
                "Request failed. {ResponseCode} {StatusCode} {Method} {Path} traceId={TraceId} correlationId={CorrelationId}",
                responseCode,
                statusCode,
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier,
                correlationId);

            if (context.Response.HasStarted)
            {
                return;
            }

            var body = new ErrorResponse
            {
                Status = false,
                StatusCode = statusCode,
                ResponseCode = responseCode,
                Message = _options.IncludeExceptionMessage
                    ? exception.Message
                    : ExceptionMapping.DefaultMessage(statusCode),
                TraceId = correlationId,
            };

            context.Response.Clear();
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception writeEx)
        {
            _logger.LogError(writeEx, "Failed to write the error response.");
        }
    }

    private (int StatusCode, string ResponseCode) Resolve(Exception exception)
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

    private string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_options.CorrelationIdHeader, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.ToString();
        }

        return context.TraceIdentifier;
    }
}
