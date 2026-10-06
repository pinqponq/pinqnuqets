using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinqponq.LiveKit.Server.Serialization;

/// <summary>
/// Reads protobuf enums written either by name (SCREEN_SHARE) or by number.
/// Names this SDK does not know yet map to the enum's <c>Unrecognized</c> member instead of failing,
/// so a newer LiveKit server does not break deserialization.
/// </summary>
internal sealed class ProtoEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(ProtoEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!; // ProtoEnumConverter<T> has a public parameterless constructor.
    }

    private sealed class ProtoEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        private const string UNRECOGNIZED_MEMBER_NAME = "Unrecognized";

        private static readonly Dictionary<string, TEnum> ValueByProtoName = Enum.GetValues<TEnum>()
            .ToDictionary(value => ToProtoName(value), StringComparer.Ordinal);

        private static readonly TEnum UnrecognizedValue = Enum.TryParse<TEnum>(UNRECOGNIZED_MEMBER_NAME, out var unrecognized)
            ? unrecognized
            : throw new InvalidOperationException($"{typeof(TEnum).Name} must declare an '{UNRECOGNIZED_MEMBER_NAME}' member.");

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), reader.GetInt32());
            }

            var protoName = reader.GetString();
            return protoName is not null && ValueByProtoName.TryGetValue(protoName, out var value) ? value : UnrecognizedValue;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ToProtoName(value));

        private static string ToProtoName(TEnum value) => JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());
    }
}
