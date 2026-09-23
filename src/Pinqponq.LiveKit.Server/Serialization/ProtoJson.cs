using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pinqponq.LiveKit.Server.Serialization;

/// <summary>
/// JSON settings compatible with protobuf's canonical JSON mapping as produced by LiveKit.
/// </summary>
internal static class ProtoJson
{
    /// <summary>
    /// Requests are sent in camelCase; LiveKit accepts both camelCase and snake_case keys.
    /// </summary>
    public static readonly JsonSerializerOptions RequestOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Twirp responses use snake_case unless the server enables camelCase, webhooks always use camelCase,
    /// and 64-bit integers arrive as strings. Every property therefore also accepts its snake_case alias.
    /// </summary>
    public static readonly JsonSerializerOptions ResponseOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new ProtoEnumConverterFactory() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { AddSnakeCaseAliases } }
    };

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, ResponseOptions)
        ?? throw new JsonException($"LiveKit returned an empty JSON document for {typeof(T).Name}.");

    private static void AddSnakeCaseAliases(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var aliases = new List<JsonPropertyInfo>();
        foreach (var property in typeInfo.Properties)
        {
            var snakeCaseName = JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            if (snakeCaseName == property.Name || property.Set is null)
            {
                continue;
            }

            var alias = typeInfo.CreateJsonPropertyInfo(property.PropertyType, snakeCaseName);
            alias.Set = property.Set;
            alias.CustomConverter = property.CustomConverter;
            alias.NumberHandling = property.NumberHandling;
            aliases.Add(alias);
        }

        foreach (var alias in aliases)
        {
            typeInfo.Properties.Add(alias);
        }
    }
}
