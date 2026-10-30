using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pinqponq.Contracts;

namespace Pinqponq.ErrorHandling;

internal sealed class ResponseCodeExceptionResolver(IOptions<ResponseCodeExceptionOptions> options) : IExceptionResolver
{
    private readonly ResponseCodeExceptionOptions _options = options.Value;

    public ExceptionResolution? Resolve(Exception exception)
    {
        var statusCode = FindStatusCode(exception.GetType());
        if (statusCode is null)
        {
            return null;
        }

        if (_options.ResponseCodes.Contains(exception.Message) || _options.UnknownMessageBehavior == UnknownMessageBehavior.PassMessageThrough)
        {
            return new ExceptionResolution(statusCode.Value, exception.Message);
        }

        return new ExceptionResolution(StatusCodes.Status500InternalServerError, CommonErrorCodes.GeneralError);
    }

    private int? FindStatusCode(Type exceptionType)
    {
        for (var type = exceptionType; type is not null; type = type.BaseType)
        {
            if (_options.StatusCodesByExceptionType.TryGetValue(type, out var statusCode))
            {
                return statusCode;
            }
        }

        return null;
    }
}
