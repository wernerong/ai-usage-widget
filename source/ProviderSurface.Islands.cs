using Avalonia;
using Avalonia.Media;

namespace UsageWidget;

internal sealed partial class ProviderSurface
{
    internal string? HoveredIslandProviderId { get; private set; }
    internal double IslandWidthLimit { get; set; } = double.PositiveInfinity;
    private bool IslandPills => monitor.Preferences.IslandStyle == IslandStyle.ProviderPills;
    internal bool SetIslandHover(string? id)
    {
        id = Layout == WidgetLayout.AdaptiveIsland && VisibleProviders.Any(p => p.Id == id) ? id : null;
        if (HoveredIslandProviderId == id) return false;
        HoveredIslandProviderId = id;
        return true;
    }
    private double CompactProviderWidth(ProviderDefinition p) => 30 + CompactQuotas(p).Length * (CompactQuotas(p).Length > 1 ? 62 : 44);
    private Quota[] NamedIslandQuotas(ProviderDefinition p) => Layout == WidgetLayout.AdaptiveIsland ? CompactQuotas(p) : PrimaryQuotas(p);
    private static double NamedQuotaWidth(Quota q) => Math.Min(32, Text(Label(q.Label), 10, Colors.Black).Width) + 4 + 40 + 8;
    private static double NamedNameWidth(ProviderDefinition p) => Math.Min(110, Text(p.Name, 12, Colors.Black, true).Width);
    internal string IslandResetText(ProviderDefinition p)
    {
        var state = monitor.States[p.Id];
        if (state.Reading == null) return Updating ? "Loading" : "No data";
        if (state.Stale) return "STALE";
        var reset = state.Reading.Quotas.Where(q => q.Reset.HasValue).Select(q => q.Reset!.Value).Order().FirstOrDefault();
        if (reset == default) return "";
        return Widget.ResetText(reset, DateTimeOffset.Now).Replace("Resets in ", "reset ").Replace("Reset due · awaiting update", "reset due");
    }
    private double NamedProviderWidth(ProviderDefinition p, bool adaptive) => 27 + NamedNameWidth(p) + 10
        + NamedIslandQuotas(p).Sum(NamedQuotaWidth) + (adaptive ? 108 : 0) + 4;
    internal double DisplayIslandWidth(ProviderDefinition p) => IslandDisplayWidth(p, HoveredIslandProviderId);
    private double IslandDisplayWidth(ProviderDefinition p, string? hover) => Layout switch
    {
        WidgetLayout.IslandBar => IslandProviderWidth(p),
        WidgetLayout.SpotlightIsland => NamedProviderWidth(p, false),
        WidgetLayout.AdaptiveIsland when p.Id == hover => Math.Max(CompactProviderWidth(p), NamedProviderWidth(p, true)),
        _ => CompactProviderWidth(p)
    };
    private sealed record IslandMetrics(Size Size, (ProviderDefinition Provider, Rect Bounds)[] Regions, Rect Pager);
    private IslandMetrics IslandGeometry() => IslandGeometry(HoveredIslandProviderId);
    private IslandMetrics IslandGeometry(string? hover)
    {
        var providers = VisibleProviders;
        var widths = providers.Select(p => IslandDisplayWidth(p, hover)).ToArray();
        var padding = IslandPills ? 0 : 24;
        var extras = IslandPills ? providers.Length * 16 + Math.Max(0, providers.Length - 1) * 5 + (PageCount > 1 ? 31 : 0) : PagerWidth;
        // Only the expanded provider loses optional name/reset space on small displays.
        // Neighbouring providers retain their compact widths.
        var named = Array.FindIndex(providers, p => Layout == WidgetLayout.SpotlightIsland || Layout == WidgetLayout.AdaptiveIsland && p.Id == hover);
        if (named >= 0 && widths.Sum() + padding + extras > IslandWidthLimit)
            widths[named] = Math.Max(CompactProviderWidth(providers[named]), IslandWidthLimit - padding - extras - widths.Where((_, i) => i != named).Sum());
        var regions = new (ProviderDefinition Provider, Rect Bounds)[providers.Length];
        double x = IslandPills ? 0 : 12;
        for (var i = 0; i < providers.Length; i++)
        {
            var width = widths[i] + (IslandPills ? 16 : 0);
            regions[i] = (providers[i], new Rect(x, 0, width, IslandHeight));
            x += width + (IslandPills && i < providers.Length - 1 ? 5 : 0);
        }
        var total = x + (IslandPills ? PageCount > 1 ? 31 : 0 : 12 + PagerWidth);
        var pager = PageCount > 1 ? IslandPills ? new Rect(x + 5, 0, 26, IslandHeight)
            : new Rect(total - 25, IslandHeight / 2 - 13, 24, 26) : default;
        return new(new(total, IslandHeight), regions, pager);
    }
    internal Size CollapsedIslandSize => IslandGeometry(null).Size;
    internal (ProviderDefinition Provider, Rect Bounds)[] IslandProviderRegions => IslandGeometry().Regions;
    internal Rect IslandPagerBounds => IslandGeometry().Pager;
    private Color IslandValueColor(ProviderDefinition p, Quota q) => monitor.States[p.Id].Stale || q.Used >= 75
        ? QuotaColor(p.Accent, q, monitor.States[p.Id].Stale) : Notice != null ? Amber : Main;
    private void DrawIslandContainer(DrawingContext g, Rect bounds, bool pill) =>
        Round(g, bounds.Deflate(.5), pill ? IslandHeight / 2 : 8, Bg, Tone("#CCD2DD", "#454B58"));
    private void RenderIsland(DrawingContext g)
    {
        var geometry = IslandGeometry();
        if (!IslandPills) DrawIslandContainer(g, new Rect(geometry.Size), false);
        foreach (var (p, bounds) in geometry.Regions)
        {
            if (IslandPills) DrawIslandContainer(g, bounds, true);
            else if (bounds.X > 12)
                g.DrawLine(new Pen(Brush(Tone("#E1E4EB", "#414753"))), new(bounds.X - 6, IslandHeight / 2 - 8), new(bounds.X - 6, IslandHeight / 2 + 8));
            var content = IslandPills ? bounds.Deflate(new Thickness(8, 0)) : bounds;
            switch (Layout)
            {
                case WidgetLayout.IslandBar: DrawDetailedIslandProvider(g, p, content); break;
                case WidgetLayout.SpotlightIsland: DrawNamedIslandProvider(g, p, content, false); break;
                case WidgetLayout.AdaptiveIsland when p.Id == HoveredIslandProviderId: DrawNamedIslandProvider(g, p, content, true); break;
                default: DrawCompactIslandProvider(g, p, content); break;
            }
        }
        if (PageCount > 1)
        {
            var pager = geometry.Pager;
            if (IslandPills) DrawIslandContainer(g, pager, true);
            else Round(g, new(pager.Center.X - 10, pager.Center.Y - 10, 20, 20), 10, Tone("#E4E7ED", "#343945"));
            Txt(g, "›", pager.Center.X - 4, pager.Center.Y - 11, 16, Muted, true);
        }
    }
    private void DrawDetailedIslandProvider(DrawingContext g, ProviderDefinition p, Rect bounds)
    {
        var state = monitor.States[p.Id]; var x = bounds.X; var center = IslandHeight / 2;
        Logo(g, p, new(x + 2, center - 12, 12, 12));
        Txt(g, p.Name, x + 19, center - 13, 10, Main, true);
        var status = state.Reading == null ? Updating ? "Loading" : "No data" : state.Stale ? "STALE" : monitor.Preferences.ShowUsed ? "% used" : "% left";
        Txt(g, status, x + 2, center + 3, 9, state.Stale || Notice != null ? Amber : Muted);
        for (var i = 0; i < p.QuotaLabels.Length; i++)
        {
            var q = state.Reading?.Quotas.ElementAtOrDefault(i) ?? new(p.QuotaLabels[i], null, null);
            var left = x + IslandNameWidth(p) + i * 64;
            Fit(g, Label(q.Label), new(left, center - 15, 56, 12), 9, Muted);
            Txt(g, PercentageText(q), left, center - 4, 14, IslandValueColor(p, q), true);
            Progress(g, p, q, state.Stale, new(left, center + 14, 48, 2));
        }
    }
    private void DrawCompactIslandProvider(DrawingContext g, ProviderDefinition p, Rect bounds)
    {
        var center = IslandHeight / 2; var left = bounds.X + 23; var quotas = CompactQuotas(p);
        Logo(g, p, new(bounds.X, center - 8, 16, 16));
        foreach (var q in quotas)
        {
            if (quotas.Length > 1) { Fit(g, Label(q.Label), new(left, center - 5, 18, 12), 9, Muted); left += 18; }
            Txt(g, PercentageText(q), left, center - 9, 13, IslandValueColor(p, q), true);
            if (monitor.States[p.Id].Stale) g.DrawEllipse(Brush(Amber), null, new Point(left + 14, center + 11), 1.5, 1.5);
            left += 44;
        }
    }
    private void DrawNamedIslandProvider(DrawingContext g, ProviderDefinition p, Rect bounds, bool adaptive)
    {
        var center = IslandHeight / 2; var quotas = NamedIslandQuotas(p);
        var quotaWidth = quotas.Sum(NamedQuotaWidth);
        var available = bounds.Width - 27 - quotaWidth - 4;
        // Drop optional reset text before the provider name, then elide only the name.
        var nameWidth = Math.Min(NamedNameWidth(p), Math.Max(0, available - 10));
        var tailWidth = adaptive ? Math.Max(0, available - nameWidth - 10) : 0;
        if (nameWidth < 18) { DrawCompactIslandProvider(g, p, bounds); return; }
        Logo(g, p, new(bounds.X, center - 8, 16, 16));
        var left = bounds.X + 27;
        Fit(g, p.Name, new(left, center - 8, nameWidth, 17), 12, Main, true); left += nameWidth + 10;
        foreach (var q in quotas)
        {
            var labelWidth = NamedQuotaWidth(q) - 52;
            Fit(g, Label(q.Label), new(left, center - 6, labelWidth, 13), 10, Muted);
            Txt(g, PercentageText(q), left + labelWidth + 4, center - 9, 13, IslandValueColor(p, q), true);
            left += NamedQuotaWidth(q);
        }
        if (tailWidth >= 28)
            Fit(g, IslandResetText(p), new(left, center - 6, tailWidth, 14), 10, monitor.States[p.Id].Stale ? Amber : Muted);
        else if (monitor.States[p.Id].Stale)
            g.DrawEllipse(Brush(Amber), null, new Point(bounds.Right - 3, center + 11), 1.5, 1.5);
    }
}
