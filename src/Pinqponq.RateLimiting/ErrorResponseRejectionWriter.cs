using Microsoft.AspNetCore.Http;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling;

namespace Pinqponq.RateLimiting;

internal sealed class ErrorResponseRejectionWriter(IErrorResponseWriter errorResponseWriter) : IRateLimitRejectionWriter
{
    public const string DefaultMessage = "Too many requests. Try again later.";

    public Task Write(HttpContext context, int? retryAfterSeconds, CancellationToken cancellationToken) =>
        errorResponseWriter.Write(
            context,
            StatusCodes.Status429TooManyRequests,
            CommonErrorCodes.TooManyRequests,
            DefaultMessage,
            RateLimitingEvents.RequestRateLimited);
}
