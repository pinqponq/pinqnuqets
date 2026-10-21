using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.ErrorHandling;

internal sealed class LocalizedErrorResponseWriter : IErrorResponseWriter
{
    private readonly ErrorHandlingOptions _options;
    private readonly ErrorLocalizationOptions _localizationOptions;
    private readonly ErrorMessageCatalog _errorMessageCatalog;

    public LocalizedErrorResponseWriter(
        IOptions<ErrorHandlingOptions> options,
        IOptions<ErrorLocalizationOptions> localizationOptions,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _localizationOptions = localizationOptions.Value;
        _errorMessageCatalog = new ErrorMessageCatalog(localizationOptions, loggerFactory.CreateLogger<ErrorMessageCatalog>());
    }

    public async Task Write(HttpContext context, int statusCode, string responseCode, string defaultMessage, string eventName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseCode);
        ArgumentNullException.ThrowIfNull(defaultMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var language = _localizationOptions.Enabled
            ? ErrorMessageCatalog.ResolveRequestLanguage(context, _localizationOptions)
            : null;
        var message = language is null
            ? defaultMessage
            : FindLocalizedMessage(responseCode, language);

        RequestErrorDetail.Describe(context, eventName, statusCode, responseCode, message, language);
        await ErrorResponseWriter.Write(context, _options, statusCode, responseCode, message).ConfigureAwait(false);
    }

    private string FindLocalizedMessage(string responseCode, string language) =>
        _errorMessageCatalog.FindMessage(responseCode, language)
        ?? _errorMessageCatalog.FindMessage(_localizationOptions.FallbackResponseCode, language)
        ?? _localizationOptions.DefaultMessage;
}
