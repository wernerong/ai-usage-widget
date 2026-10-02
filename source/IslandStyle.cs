using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageWidget;

[JsonConverter(typeof(IslandStyleConverter))]
internal enum IslandStyle { Continuous, ProviderPills }

internal sealed class IslandStyleConverter : JsonConverter<IslandStyle>
{
    public override IslandStyle Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<IslandStyle>(reader.GetString(), true, out var style)
            && Enum.IsDefined(style)) return style;
        reader.Skip();
        return IslandStyle.Continuous;
    }
    public override void Write(Utf8JsonWriter writer, IslandStyle value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
