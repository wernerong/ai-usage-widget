using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageWidget;

[JsonConverter(typeof(WidgetLayoutConverter))]
internal enum WidgetLayout
{
    DetailedCards, RoundBadges, CompactList, TileGrid, UsageBars, Focus, StatusRail, IslandBar, CompactIsland,
    AdaptiveIsland, SpotlightIsland
}

internal static class WidgetLayouts
{
    internal static readonly WidgetLayout[] MenuOrder = [WidgetLayout.RoundBadges, WidgetLayout.DetailedCards,
        WidgetLayout.CompactList, WidgetLayout.TileGrid, WidgetLayout.UsageBars, WidgetLayout.Focus,
        WidgetLayout.StatusRail, WidgetLayout.AdaptiveIsland, WidgetLayout.SpotlightIsland, WidgetLayout.IslandBar, WidgetLayout.CompactIsland];
    internal static bool IsIsland(this WidgetLayout layout) => layout is WidgetLayout.IslandBar or WidgetLayout.CompactIsland
        or WidgetLayout.AdaptiveIsland or WidgetLayout.SpotlightIsland;
    internal static bool IsAdditional(this WidgetLayout layout) => layout is WidgetLayout.CompactList or WidgetLayout.TileGrid
        or WidgetLayout.UsageBars or WidgetLayout.Focus or WidgetLayout.StatusRail;
    internal static int PageSize(this WidgetLayout layout) => layout switch
    {
        WidgetLayout.CompactList or WidgetLayout.TileGrid or WidgetLayout.UsageBars => 6,
        WidgetLayout.Focus or WidgetLayout.SpotlightIsland => 1, WidgetLayout.StatusRail => 8, _ => 3
    };
    internal static string Title(this WidgetLayout layout) => layout switch
    {
        WidgetLayout.RoundBadges => "Round badges (compact)", WidgetLayout.DetailedCards => "Detailed cards",
        WidgetLayout.CompactList => "Compact list", WidgetLayout.TileGrid => "Tile grid", WidgetLayout.UsageBars => "Usage bars",
        WidgetLayout.Focus => "Focus mode", WidgetLayout.StatusRail => "Status rail", WidgetLayout.IslandBar => "Island bar",
        WidgetLayout.AdaptiveIsland => "Adaptive island", WidgetLayout.SpotlightIsland => "Spotlight island", _ => "Compact island"
    };
    internal static string Id(this WidgetLayout layout) => layout switch
    {
        WidgetLayout.RoundBadges => "badges", WidgetLayout.DetailedCards => "detailed", WidgetLayout.CompactList => "compact-list",
        WidgetLayout.TileGrid => "grid", WidgetLayout.UsageBars => "bars", WidgetLayout.Focus => "focus",
        WidgetLayout.StatusRail => "status-rail", WidgetLayout.IslandBar => "island",
        WidgetLayout.AdaptiveIsland => "adaptive-island", WidgetLayout.SpotlightIsland => "spotlight-island", _ => "compact-island"
    };
    internal static WidgetLayout FromArguments(string[] args, WidgetLayout saved)
    {
        foreach (var arg in args)
            saved = arg switch
            {
                "--cards" => WidgetLayout.DetailedCards, "--compact" => WidgetLayout.RoundBadges,
                "--island" => WidgetLayout.IslandBar, "--compact-island" => WidgetLayout.CompactIsland,
                "--adaptive-island" => WidgetLayout.AdaptiveIsland, "--spotlight-island" => WidgetLayout.SpotlightIsland,
                "--compact-list" => WidgetLayout.CompactList, "--grid" => WidgetLayout.TileGrid,
                "--bars" => WidgetLayout.UsageBars, "--focus" => WidgetLayout.Focus, "--rail" => WidgetLayout.StatusRail, _ => saved
            };
        return saved;
    }
}

// An unknown future layout must not discard unrelated preferences on downgrade.
internal sealed class WidgetLayoutConverter : JsonConverter<WidgetLayout>
{
    public override WidgetLayout Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<WidgetLayout>(reader.GetString(), true, out var layout)
            && Enum.IsDefined(layout)) return layout;
        reader.Skip();
        return WidgetLayout.DetailedCards;
    }
    public override void Write(Utf8JsonWriter writer, WidgetLayout value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
