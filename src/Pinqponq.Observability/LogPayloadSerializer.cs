using System.Text.Json;

namespace Pinqponq.Observability;

internal static class LogPayloadSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ToJson(object data) => JsonSerializer.Serialize(data, SerializerOptions);
}
