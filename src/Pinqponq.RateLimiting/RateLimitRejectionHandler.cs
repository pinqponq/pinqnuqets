using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Pinqponq.RateLimiting;

internal static class RateLimitRejectionHandler
{
    private const int MinimumRetryAfterSeconds = 1;

    public static async ValueTask OnRejected(OnRejectedContext rejectedContext, CancellationToken cancellationToken)
    {
        var httpContext = rejectedContext.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        int? retryAfterSeconds = rejectedContext.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? ToRetryAfterSeconds(retryAfter)
            : null;
        if (retryAfterSeconds is int headerSeconds)
        {
            httpContext.Response.Headers.RetryAfter = headerSeconds.ToString(NumberFormatInfo.InvariantInfo);
        }

        var rejectionWriter = httpContext.RequestServices.GetRequiredService<IRateLimitRejectionWriter>();
        await rejectionWriter.Write(httpContext, retryAfterSeconds, cancellationToken).ConfigureAwait(false);
    }

    // Rounded up, so a client that waits the sent number of seconds is never rejected again in the same window,
    // and never 0, which a client could read as "retry at once".
    public static int ToRetryAfterSeconds(TimeSpan retryAfter) =>
        Math.Max(MinimumRetryAfterSeconds, (int)Math.Ceiling(retryAfter.TotalSeconds));
}
