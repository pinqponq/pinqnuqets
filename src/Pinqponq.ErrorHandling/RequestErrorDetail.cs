using Microsoft.AspNetCore.Http;
using Pinqponq.Contracts;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Describes an answered failure on <see cref="IRequestErrorFeature"/>.
/// </summary>
internal static class RequestErrorDetail
{
    public static void Describe(
        HttpContext context,
        string eventName,
        int statusCode,
        string responseCode,
        string message,
        string? language = null,
        IReadOnlyDictionary<string, object?>? additionalDetail = null)
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

        context.Features.Set<IRequestErrorFeature>(new RequestErrorFeature(eventName, detail));
    }
}
