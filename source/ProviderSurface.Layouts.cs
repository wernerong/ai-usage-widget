using Avalonia;
using Avalonia.Media;

namespace UsageWidget;

internal enum SurfaceAction { None, Refresh, Hide, Menu }

internal sealed partial class ProviderSurface
{
    private const double HeaderHeight = 44, FooterHeight = 28, ListRowHeight = 48, BarRowHeight = 60,
        RailHeaderHeight = 32, RailRowHeight = 32, TileHeight = 108, TileGap = 8;
    private int GridColumns => VisibleProviders.Length == 1 ? 1 : 2;
    private Size AdditionalSize => Layout switch
    {
        WidgetLayout.CompactList => new(320, HeaderHeight + ListRowHeight * VisibleProviders.Length + FooterHeight),
        WidgetLayout.TileGrid => new(GridColumns == 1 ? 184 : 340,
            HeaderHeight + Math.Ceiling(VisibleProviders.Length / (double)GridColumns) * (TileHeight + TileGap) - TileGap + FooterHeight),
        WidgetLayout.UsageBars => new(340, HeaderHeight + BarRowHeight * VisibleProviders.Length + FooterHeight),
        WidgetLayout.Focus => new(284, 348),
        _ => new(200, RailHeaderHeight + RailRowHeight * VisibleProviders.Length + FooterHeight)
    };
    // The same rectangles drive painting and hit testing, including grid gaps.
    internal (ProviderDefinition Provider, Rect Bounds)[] ProviderRegions => VisibleProviders.Select((p, i) =>
    {
        var width = DesiredWidgetSize.Width;
        var bounds = Layout switch
        {
            WidgetLayout.CompactList => new Rect(8, HeaderHeight + i * ListRowHeight, width - 16, ListRowHeight),
            WidgetLayout.TileGrid => new Rect(10 + i % GridColumns * ((width - 20 - (GridColumns - 1) * TileGap) / GridColumns + TileGap),
                HeaderHeight + i / GridColumns * (TileHeight + TileGap), (width - 20 - (GridColumns - 1) * TileGap) / GridColumns, TileHeight),
            WidgetLayout.UsageBars => new Rect(12, HeaderHeight + i * BarRowHeight, width - 24, BarRowHeight),
            WidgetLayout.Focus => new Rect(12, HeaderHeight, width - 24, DesiredWidgetSize.Height - HeaderHeight - FooterHeight),
            _ => new Rect(8, RailHeaderHeight + i * RailRowHeight, width - 16, RailRowHeight)
        };
        return (p, bounds);
    }).ToArray();
    internal Rect PreviousPageBounds => new(DesiredWidgetSize.Width - 56, DesiredWidgetSize.Height - FooterHeight, 24, 24);
    internal Rect NextPageBounds => new(DesiredWidgetSize.Width - 30, DesiredWidgetSize.Height - FooterHeight, 24, 24);
    internal Rect HeaderButtonBounds(SurfaceAction action)
    {
        if (Layout == WidgetLayout.StatusRail || DesiredWidgetSize.Width <= 200)
            return action == SurfaceAction.Menu ? new(DesiredWidgetSize.Width - 28, 2, 26, 28) : default;
        var offset = action switch { SurfaceAction.Menu => 34, SurfaceAction.Hide => 64, SurfaceAction.Refresh => 94, _ => 0 };
        return action == SurfaceAction.None ? default : new(DesiredWidgetSize.Width - offset, 4, 28, 34);
    }
    internal SurfaceAction HeaderActionAt(Point point)
    {
        if (!Layout.IsAdditional()) return SurfaceAction.None;
        foreach (var action in new[] { SurfaceAction.Menu, SurfaceAction.Hide, SurfaceAction.Refresh })
        {
            var bounds = HeaderButtonBounds(action);
            if (bounds.Width > 0 && bounds.Contains(point)) return action;
        }
        return SurfaceAction.None;
    }
    internal double? DisplayPercentage(Quota? quota) => quota?.Note == "Unlimited" || quota?.Used is not { } used || !double.IsFinite(used)
        ? null : monitor.Preferences.ShowUsed ? Math.Clamp(used, 0, 100) : 100 - Math.Clamp(used, 0, 100);
    internal string PercentageText(Quota? quota) => quota?.Note == "Unlimited" ? "∞" : DisplayPercentage(quota) is { } value ? $"{value:0}%" : "—";
    internal Color QuotaColor(Color accent, Quota? quota, bool stale) => stale ? Amber
        : quota?.Used >= 90 ? Tone("#C14E4E", "#F18484") : quota?.Used >= 75 ? Amber : Accent(accent);
    internal Quota[] PrimaryQuotas(ProviderDefinition provider)
    {
        var quotas = monitor.States[provider.Id].Reading?.Quotas;
        return (quotas is { Length: > 0 } ? quotas : provider.QuotaLabels.Select(label => new Quota(label, null, null))).Take(2).ToArray();
    }
    internal static string FitText(string text, double width, double size, bool bold = false)
    {
        if (width <= 0) return "";
        if (Text(text, size, Colors.Black, bold).Width <= width) return text;
        while (text.Length > 0 && Text(text + "…", size, Colors.Black, bold).Width > width) text = text[..^1];
        return text.Length == 0 && Text("…", size, Colors.Black).Width > width ? "" : text + "…";
    }
    private static void Fit(DrawingContext g, string text, Rect bounds, double size, Color color, bool bold = false, TextAlignment alignment = TextAlignment.Left)
    {
        var ft = Text(FitText(text, bounds.Width, size, bold), size, color, bold);
        var x = alignment == TextAlignment.Right ? bounds.Right - ft.Width : alignment == TextAlignment.Center ? bounds.Center.X - ft.Width / 2 : bounds.X;
        g.DrawText(ft, new(x, bounds.Y));
    }
    private string Status(ProviderState state) => state.Reading == null ? state.Error != null ? "Unavailable" : Updating ? "Loading…" : "No data"
        : state.Stale ? "Stale · hover for details" : "Live";
    private void Progress(DrawingContext g, ProviderDefinition provider, Quota quota, bool stale, Rect bounds)
    {
        Round(g, bounds, bounds.Height / 2, Tone("#E2E6ED", "#3C414D"));
        if (DisplayPercentage(quota) is > 0 and var value)
            Round(g, new(bounds.X, bounds.Y, bounds.Width * value / 100, bounds.Height), bounds.Height / 2, QuotaColor(provider.Accent, quota, stale));
    }
    private void Divider(DrawingContext g, Rect bounds) => g.DrawLine(new Pen(Brush(Tone("#DADEE6", "#414650"))), bounds.TopLeft, bounds.TopRight);
    private void RenderAdditional(DrawingContext g)
    {
        var size = DesiredWidgetSize;
        var rail = Layout == WidgetLayout.StatusRail;
        Round(g, new(.5, .5, size.Width - 1, size.Height - 1), rail ? 11 : 16, Bg, Tone("#DADEE6", "#414650"));
        Txt(g, "Usage", rail ? 10 : 14, rail ? 8 : 12, rail ? 12 : 15, Main, true);
        var chip = new Rect(rail ? 62 : 70, rail ? 6 : 12, 49, 19);
        Round(g, chip, 5, Tone("#E2E6ED", "#3C414D"));
        Txt(g, monitor.Preferences.ShowUsed ? "% used" : "% left", chip.X + 6, chip.Y + 2, 10, Muted);
        foreach (var action in new[] { SurfaceAction.Refresh, SurfaceAction.Hide, SurfaceAction.Menu })
        {
            var bounds = HeaderButtonBounds(action);
            if (bounds.Width == 0) continue;
            Fit(g, action == SurfaceAction.Refresh ? Updating ? "···" : "↻" : action == SurfaceAction.Hide ? "—" : "⋮",
                new(bounds.X, bounds.Y + (rail ? 0 : 2), bounds.Width, bounds.Height), 22, Muted, alignment: TextAlignment.Center);
        }
        switch (Layout)
        {
            case WidgetLayout.CompactList: RenderCompactList(g); break;
            case WidgetLayout.TileGrid: RenderTileGrid(g); break;
            case WidgetLayout.UsageBars: RenderUsageBars(g); break;
            case WidgetLayout.Focus: RenderFocus(g); break;
            case WidgetLayout.StatusRail: RenderStatusRail(g); break;
        }
        var footer = new Rect(10, size.Height - 22, size.Width - (PageCount > 1 ? 74 : 20), 18);
        var providerCount = $"{VisibleProviders.Length} provider{(VisibleProviders.Length == 1 ? "" : "s")}";
        Fit(g, Notice != null ? "Settings error · hover" : PageCount > 1 ? $"{Page + 1}/{PageCount} · {providerCount}"
            : providerCount, footer, 10, Notice != null ? Amber : Muted);
        if (PageCount > 1)
        {
            Fit(g, "‹", PreviousPageBounds, 19, Muted, alignment: TextAlignment.Center);
            Fit(g, "›", NextPageBounds, 19, Muted, alignment: TextAlignment.Center);
        }
    }
    private void RenderCompactList(DrawingContext g)
    {
        foreach (var (p, r) in ProviderRegions)
        {
            var state = monitor.States[p.Id]; var quotas = PrimaryQuotas(p);
            Divider(g, r);
            Logo(g, p, new(r.X + 5, r.Y + 9, 18, 18));
            var quotaStart = r.Right - quotas.Length * 72;
            Fit(g, p.Name, new(r.X + 32, r.Y + 9, quotaStart - r.X - 40, 18), 12, Main, true);
            for (var i = 0; i < quotas.Length; i++)
            {
                var x = quotaStart + i * 72;
                Fit(g, Label(quotas[i].Label), new(x, r.Y + 11, 27, 16), 10, Muted);
                Fit(g, PercentageText(quotas[i]), new(x + 28, r.Y + 8, 40, 18), 13, state.Stale ? Amber : Main, true, TextAlignment.Right);
            }
            var reset = quotas.FirstOrDefault(q => q.Reset != null)?.Reset;
            var status = Status(state);
            Fit(g, status != "Live" ? status : reset == null ? "Reset time unavailable" : Widget.ResetText(reset, DateTimeOffset.Now),
                new(r.X + 32, r.Y + 28, r.Width - 38, 17), 11, state.Stale ? Amber : Muted);
        }
    }
    private void RenderTileGrid(DrawingContext g)
    {
        foreach (var (p, r) in ProviderRegions)
        {
            var state = monitor.States[p.Id]; var quotas = PrimaryQuotas(p);
            Round(g, r, 10, Card);
            Logo(g, p, new(r.X + 10, r.Y + 10, 16, 16));
            Fit(g, p.Name, new(r.X + 33, r.Y + 9, r.Width - 43, 18), 12, Main, true);
            for (var i = 0; i < quotas.Length; i++)
            {
                var y = r.Y + 35 + i * 28;
                Fit(g, quotas[i].Label, new(r.X + 10, y, r.Width - 60, 17), 11, Muted);
                Fit(g, PercentageText(quotas[i]), new(r.Right - 47, y - 2, 37, 18), 13, state.Stale ? Amber : Main, true, TextAlignment.Right);
                Progress(g, p, quotas[i], state.Stale, new(r.X + 10, y + 19, r.Width - 20, 4));
            }
            if (Status(state) != "Live") Fit(g, state.Reading != null && state.Stale ? "Stale" : Status(state), new(r.X + 10, r.Bottom - 18, r.Width - 20, 16), 10, state.Stale ? Amber : Muted);
        }
    }
    private void RenderUsageBars(DrawingContext g)
    {
        foreach (var (p, r) in ProviderRegions)
        {
            var state = monitor.States[p.Id]; var quotas = PrimaryQuotas(p);
            Divider(g, r);
            Logo(g, p, new(r.X, r.Y + 17, 16, 16));
            Fit(g, p.Name, new(r.X + 22, r.Y + 16, 73, 17), 11, Main, true);
            if (Status(state) != "Live") Fit(g, state.Reading != null && state.Stale ? "Stale" : Status(state), new(r.X + 22, r.Y + 34, 73, 14), 10, state.Stale ? Amber : Muted);
            for (var i = 0; i < quotas.Length; i++)
            {
                var y = r.Y + (quotas.Length == 1 ? 23 : 12 + 21 * i);
                Fit(g, Label(quotas[i].Label), new(r.X + 101, y, 34, 16), 10, Muted);
                Progress(g, p, quotas[i], state.Stale, new(r.X + 141, y + 5, r.Width - 181, 6));
                Fit(g, PercentageText(quotas[i]), new(r.Right - 36, y - 1, 36, 18), 12, Main, true, TextAlignment.Right);
            }
        }
    }
    private void RenderFocus(DrawingContext g)
    {
        foreach (var (p, r) in ProviderRegions)
        {
            var state = monitor.States[p.Id]; var quotas = PrimaryQuotas(p);
            Logo(g, p, new(r.Center.X - 19, r.Y + 10, 38, 38));
            Fit(g, p.Name, new(r.X, r.Y + 56, r.Width, 24), 18, Main, true, TextAlignment.Center);
            Fit(g, Status(state) == "Live" ? "Subscription usage" : Status(state), new(r.X, r.Y + 82, r.Width, 18), 11, state.Stale ? Amber : Muted, alignment: TextAlignment.Center);
            var width = quotas.Length == 1 ? 180 : (r.Width - 12) / 2;
            for (var i = 0; i < quotas.Length; i++)
            {
                var x = quotas.Length == 1 ? r.Center.X - width / 2 : r.X + i * (width + 12);
                Fit(g, PercentageText(quotas[i]), new(x, r.Y + 113, width, 42), 34, state.Stale ? Amber : Main, true, TextAlignment.Center);
                Fit(g, quotas[i].Label, new(x, r.Y + 157, width, 18), 11, Muted, alignment: TextAlignment.Center);
                Progress(g, p, quotas[i], state.Stale, new(x, r.Y + 182, width, 5));
                Fit(g, quotas[i].Note == "Unlimited" ? "Unlimited" : Widget.ResetText(quotas[i].Reset, DateTimeOffset.Now), new(x, r.Y + 196, width, 18), 10, Muted, alignment: TextAlignment.Center);
            }
            if (p.HasFreeResets) Fit(g, $"Free resets  {state.Reading?.FreeResets?.ToString() ?? "—"}", new(r.X, r.Y + 229, r.Width, 18), 11, Muted, alignment: TextAlignment.Center);
            if (state.Reading?.Balance is { } balance) Fit(g, balance, new(r.X, r.Y + 249, r.Width, 18), 11, Muted, alignment: TextAlignment.Center);
        }
    }
    private void RenderStatusRail(DrawingContext g)
    {
        foreach (var (p, r) in ProviderRegions)
        {
            var state = monitor.States[p.Id]; var quotas = PrimaryQuotas(p);
            Logo(g, p, new(r.X + 1, r.Y + 9, 14, 14));
            var quotaStart = r.Right - quotas.Length * 38;
            Fit(g, p.Name, new(r.X + 21, r.Y + 8, quotaStart - r.X - 26, 18), 11, Main, true);
            for (var i = 0; i < quotas.Length; i++)
            {
                var x = quotaStart + i * 38;
                Fit(g, Label(quotas[i].Label), new(x, r.Y + 1, 35, 14), 10, Muted, alignment: TextAlignment.Right);
                Fit(g, PercentageText(quotas[i]), new(x, r.Y + 14, 35, 17), 12, state.Stale ? Amber : Main, true, TextAlignment.Right);
            }
            if (state.Stale) g.DrawEllipse(Brush(Amber), null, new(r.X + 16, r.Y + 25), 1.5, 1.5);
        }
    }
}
