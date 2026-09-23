using Pinqponq.LiveKit.Server.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinqponq.LiveKit.Server.Auth;

/// <summary>
/// Token grants carry sources in lowercase (screen_share), unlike the API models which use SCREEN_SHARE.
/// </summary>
internal sealed class TrackSourceGrantListConverter : JsonConverter<IReadOnlyList<TrackSource>>
{
    private static readonly TrackSource[] GrantableSources =
    [
        TrackSource.Camera,
        TrackSource.Microphone,
        TrackSource.ScreenShare,
        TrackSource.ScreenShareAudio
    ];

    private static readonly Dictionary<TrackSource, string> GrantNameBySource = GrantableSources
        .ToDictionary(source => source, source => JsonNamingPolicy.SnakeCaseLower.ConvertName(source.ToString()));

    private static readonly Dictionary<string, TrackSource> SourceByGrantName = GrantNameBySource
        .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public override IReadOnlyList<TrackSource> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var grantNames = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? [];
        return grantNames
            .Select(grantName => SourceByGrantName.TryGetValue(grantName, out var source) ? source : TrackSource.Unrecognized)
            .ToList();
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<TrackSource> sources, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var source in sources)
        {
            if (!GrantNameBySource.TryGetValue(source, out var grantName))
            {
                throw new ArgumentException($"Track source '{source}' cannot be granted.", nameof(sources));
            }

            writer.WriteStringValue(grantName);
        }

        writer.WriteEndArray();
    }
}
