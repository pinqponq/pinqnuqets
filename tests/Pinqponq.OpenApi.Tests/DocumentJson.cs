using System.Text.Json;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

internal static class DocumentJson
{
    public static JsonElement Operation(this JsonDocument document, string path, string method = "get") =>
        document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);

    public static bool TryGetSecurity(this JsonDocument document, string path, out JsonElement security) =>
        document.Operation(path).TryGetProperty("security", out security);

    public static bool TryGetSecurityScheme(this JsonDocument document, string schemeName, out JsonElement scheme)
    {
        scheme = default;
        return document.RootElement.TryGetProperty("components", out var components)
            && components.TryGetProperty("securitySchemes", out var schemes)
            && schemes.TryGetProperty(schemeName, out scheme);
    }

    public static string[] RequirementSchemeNames(this JsonElement security) =>
        security.EnumerateArray()
            .Select(requirement => Assert.Single(requirement.EnumerateObject()).Name)
            .ToArray();
}
