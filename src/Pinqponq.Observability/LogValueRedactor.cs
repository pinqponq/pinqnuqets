using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Pinqloq;

namespace Pinqponq.Observability;

/// <summary>
/// Turns logged bodies and query strings into log values: JSON and form bodies are parsed, sensitive
/// keys become <c>***REDACTED***</c>, URL values become <c>****</c> and OpenAPI documents are omitted.
/// </summary>
internal static class LogValueRedactor
{
    private const string RedactedValue = "***REDACTED***";
    private const string RedactedUrl = "****";
    private const string OpenApiBodyOmitted = "[openapi document omitted]";
    private const string OpenApiVersionProperty = "openapi";
    private const string OpenApiPathsProperty = "paths";
    private const string JsonObjectStart = "{";
    private const string JsonArrayStart = "[";
    private const string SecretKeyToken = "secret";
    private const string TokenKeyToken = "token";
    private const string PasswordKeyToken = "password";

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Internal-Key",
        "ApiKey",
        "X-Api-Key"
    };

    public static object? FormatBody(string? contentType, string bodyText, PinqloqRedactionPlan redactionPlan)
    {
        if (TryParseJsonBody(bodyText, redactionPlan, out var jsonBody))
        {
            return jsonBody;
        }

        if (contentType?.Contains(MediaTypeNames.Application.FormUrlEncoded, StringComparison.OrdinalIgnoreCase) is true)
        {
            return QueryHelpers.ParseQuery(bodyText).ToDictionary(
                formParameter => formParameter.Key,
                formParameter => SanitizeValue(formParameter.Key, formParameter.Value.ToString(), redactionPlan));
        }

        return bodyText;
    }

    public static Dictionary<string, object?> SanitizeQuery(IQueryCollection query, PinqloqRedactionPlan redactionPlan)
    {
        return query.ToDictionary(
            queryParameter => queryParameter.Key,
            queryParameter => SanitizeValue(queryParameter.Key, queryParameter.Value.ToString(), redactionPlan));
    }

    private static bool TryParseJsonBody(string bodyText, PinqloqRedactionPlan redactionPlan, out object? jsonBody)
    {
        jsonBody = null;
        var trimmedBody = bodyText.TrimStart();
        if (!trimmedBody.StartsWith(JsonObjectStart, StringComparison.Ordinal) &&
            !trimmedBody.StartsWith(JsonArrayStart, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var parsedJsonDocument = JsonDocument.Parse(bodyText);
            if (parsedJsonDocument.RootElement.ValueKind is JsonValueKind.Object
                && parsedJsonDocument.RootElement.TryGetProperty(OpenApiVersionProperty, out _)
                && parsedJsonDocument.RootElement.TryGetProperty(OpenApiPathsProperty, out _))
            {
                jsonBody = OpenApiBodyOmitted;
                return true;
            }

            jsonBody = SanitizeJsonElement(parsedJsonDocument.RootElement, redactionPlan);
            return true;
        }
        // A body that only starts like JSON is logged as plain text instead.
        catch (JsonException)
        {
            return false;
        }
    }

    private static object? SanitizeJsonElement(JsonElement element, PinqloqRedactionPlan redactionPlan)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                jsonProperty => jsonProperty.Name,
                jsonProperty => IsSensitiveKey(jsonProperty.Name, redactionPlan)
                    ? RedactedValue
                    : SanitizeJsonElement(jsonProperty.Value, redactionPlan)),
            JsonValueKind.Array => element.EnumerateArray().Select(arrayElement => SanitizeJsonElement(arrayElement, redactionPlan)).ToList(),
            JsonValueKind.String => SanitizeStringValue(element.GetString()),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static object? SanitizeStringValue(string? stringToSanitize)
    {
        if (IsUrlValue(stringToSanitize))
        {
            return RedactedUrl;
        }

        return stringToSanitize;
    }

    private static bool IsUrlValue(string? stringToCheck)
    {
        if (string.IsNullOrEmpty(stringToCheck))
        {
            return false;
        }

        return stringToCheck.StartsWith(Uri.UriSchemeHttp + Uri.SchemeDelimiter, StringComparison.OrdinalIgnoreCase)
               || stringToCheck.StartsWith(Uri.UriSchemeHttps + Uri.SchemeDelimiter, StringComparison.OrdinalIgnoreCase);
    }

    private static object? SanitizeValue(string fieldKey, string? queryParameterValue, PinqloqRedactionPlan redactionPlan)
    {
        if (IsSensitiveKey(fieldKey, redactionPlan))
        {
            return RedactedValue;
        }

        if (IsUrlValue(queryParameterValue))
        {
            return RedactedUrl;
        }

        return queryParameterValue;
    }

    private static bool IsSensitiveKey(string fieldKey, PinqloqRedactionPlan redactionPlan)
    {
        if (SensitiveKeys.Contains(fieldKey))
        {
            return true;
        }

        if (fieldKey.Contains(SecretKeyToken, StringComparison.OrdinalIgnoreCase)
            || fieldKey.Contains(TokenKeyToken, StringComparison.OrdinalIgnoreCase)
            || fieldKey.Contains(PasswordKeyToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return redactionPlan.ShouldRedact(fieldKey);
    }
}
