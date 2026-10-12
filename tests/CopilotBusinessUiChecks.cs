using Avalonia;
using Avalonia.Media.Imaging;
using UsageWidget;
using System.Text.Json;

internal static class CopilotBusinessUiChecks
{
    internal static void Run()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        using var grok = new GrokProvider();
        var provider = Widget.CreateProviders(grok).Single(p => p.Id == "copilot");
        var prefs = new Preferences(); prefs.SetEnabled(provider, true);
        using var monitor = new UsageMonitor(prefs, [provider]);
        var widget = new Widget(["--render-check"], monitor, createTray: false, persist: false);
        try
        {
            foreach (var (data, expected, label) in new[] {
                ("""{"token_based_billing":true,"copilot_plan":"business","quota_snapshots":{"premium_interactions":{"percent_remaining":64.6,"entitlement":1000,"credits_used":97}}}""", "65%", "capped"),
                ("""{"token_based_billing":true,"copilot_plan":"business","quota_snapshots":{"premium_interactions":{"unlimited":true,"has_quota":true,"credits_used":97}}}""", "No cap", "uncapped"),
                ("""{"token_based_billing":true,"quota_snapshots":{"premium_interactions":{"unlimited":true,"has_quota":false}}}""", "Empty", "pool-empty"),
                ("""{"quota_snapshots":{"premium_interactions":{"percent_remaining":64.6},"chat":{"unlimited":true}}}""", "65%", "legacy") })
            {
                using var json = JsonDocument.Parse(data);
                var reading = CopilotProvider.Parse(json.RootElement);
                monitor.States[provider.Id].Reading = reading;
                foreach (var layout in Enum.GetValues<WidgetLayout>())
                {
                    widget.SetLayout(layout); widget.SetPercentage(false);
                    Check(widget.Surface.PercentageText(reading.Quotas[0]) == expected, $"{layout}: whole-number percentage or explicit {label} status");
                    Check(widget.Surface.CompactQuotas(provider).First() == reading.Quotas[0], $"{layout}: summary represents the main allowance");
                    Check(widget.Details(provider).Contains(label == "uncapped" ? "No personal cap" : label == "pool-empty" ? "shared credit limit" : "35% used · 65% left"), $"{layout}: Business scope and rounding survive in tooltip");
                    widget.Surface.Measure(new(widget.Width, widget.Height)); widget.Surface.Arrange(new(0, 0, widget.Width, widget.Height));
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)widget.Width, (int)widget.Height), new(96, 96));
                    bitmap.Render(widget.Surface);
                    var region = layout.IsAdditional() ? widget.Surface.ProviderRegions[0].Bounds
                        : layout.IsIsland() ? widget.Surface.IslandProviderRegions[0].Bounds
                        : layout == WidgetLayout.RoundBadges ? new Rect(0, 0, 96, 96) : new Rect(12, 48, 296, provider.CardHeight);
                    Check(widget.Surface.ProviderAt(region.Center) == provider, $"{layout}: rendered Copilot region opens the correct usage tooltip");
                    if (layout == WidgetLayout.DetailedCards && label is "capped" or "uncapped") widget.SaveRender("copilot-business-" + label + ".png");
                }
                widget.SetPercentage(true);
                Check(widget.Surface.PercentageText(reading.Quotas[0]) == (expected == "65%" ? "35%" : expected), $"{label}: used display preserves explicit statuses");
            }
            Console.WriteLine($"PASS: {count} Copilot layout, percentage, tooltip, and snapshot rendering checks.");
        }
        finally { widget.PrepareExit(); widget.Close(); }
    }
}
