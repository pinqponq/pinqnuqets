using Microsoft.AspNetCore.Http;

namespace Pinqponq.RateLimiting;

/// <summary>
/// Writes the body of a rejected request. The status code (429) and the <c>Retry-After</c> header
/// are already set when it runs. The default writes the standard error body with the
/// <c>too_many_requests</c> code. Register your own implementation to return another body.
/// </summary>
public interface IRateLimitRejectionWriter
{
    /// <param name="context">The rejected request.</param>
    /// <param name="retryAfterSeconds">The value of the <c>Retry-After</c> header, or <see langword="null"/> when the limiter gave none and no header was set.</param>
    /// <param name="cancellationToken">Cancelled when the request is aborted.</param>
    Task Write(HttpContext context, int? retryAfterSeconds, CancellationToken cancellationToken);
}
