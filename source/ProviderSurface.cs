using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace UsageWidget;

internal sealed class ProviderSurface : Control, IDisposable
{
    private readonly UsageMonitor monitor;
    private readonly Dictionary<string, Bitmap> logos = new();
    private int page;
    internal int PageCount => Math.Max(1, (monitor.Enabled.Length + 2) / 3);
    internal int Page => Math.Clamp(page, 0, PageCount - 1);
    internal ProviderDefinition[] VisibleProviders => monitor.Enabled.Skip(Page * 3).Take(3).ToArray();
    internal void ChangePage(int direction) => page = (Page + direction + PageCount) % PageCount;
    private Color Tone(string light, string dark) => Color.Parse(monitor.Preferences.DarkMode ? dark : light);
    private Color Bg => Tone("#F2F3F6", "#202228");
    private Color Card => Tone("#FDFDFE", "#292C34");
    private Color Main => Tone("#1E2026", "#F0F2F7");
    private Color Muted => Tone("#717681", "#AEB5C4");
    private Color Amber => Tone("#AF6F1E", "#EDBA6B");
    private Color Accent(Color color) => !monitor.Preferences.DarkMode ? color : Color.FromRgb(
        (byte)(color.R + (255 - color.R) * 0.35), (byte)(color.G + (255 - color.G) * 0.35), (byte)(color.B + (255 - color.B) * 0.35));
    private static readonly Typeface Regular = new("Inter"), Bold = new("Inter", FontStyle.Normal, FontWeight.Bold);
    public bool Updating { get; set; }
    public string? Notice { get; set; }
    internal double IslandHeight { get; set; } = 40;
    public double CardsHeight => 63 + VisibleProviders.Sum(p => p.CardHeight + 8);
    private static double IslandNameWidth(ProviderDefinition p) => Math.Max(60, 27 + Text(p.Name, 10, Colors.Black, true).Width);
    internal static double IslandProviderWidth(ProviderDefinition p) => IslandNameWidth(p) + 64 * p.QuotaLabels.Length;
    internal Quota[] CompactQuotas(ProviderDefinition p)
    {
        var quotas = monitor.States[p.Id].Reading?.Quotas ?? [];
        var weekly = quotas.FirstOrDefault(q => q.Label == "Weekly") ?? new Quota("Weekly", null, null);
        var fiveHour = quotas.FirstOrDefault(q => q.Label == "5 hours" && q.Used is not null);
        if (p.QuotaLabels.Contains("Weekly")) return fiveHour != null ? [fiveHour, weekly] : [weekly];
        var known = quotas.Where(q => q.Used != null || q.Note == "Unlimited").ToArray();
        return known.Length > 0 ? [known.OrderByDescending(q => q.Used).First()] : [new(p.QuotaLabels[0], null, null)];
    }
    internal double DisplayIslandWidth(ProviderDefinition p) => monitor.Preferences.CompactIsland
        ? 30 + CompactQuotas(p).Length * (CompactQuotas(p).Length > 1 ? 62 : 44) : IslandProviderWidth(p);
    private double PagerWidth => PageCount > 1 ? 26 : 0;
    public Size DesiredWidgetSize => monitor.Preferences.Island ? new(24 + VisibleProviders.Sum(DisplayIslandWidth) + PagerWidth, IslandHeight) : monitor.Preferences.Compact ? new(VisibleProviders.Length * 100 - 4 + PagerWidth, 96) : new(320, CardsHeight);
    internal bool IsPager(Point point) => PageCount > 1 && (monitor.Preferences.Island || monitor.Preferences.Compact
        ? new Rect(DesiredWidgetSize.Width - 25, DesiredWidgetSize.Height / 2 - 13, 24, 26).Contains(point)
        : new Rect(12, CardsHeight - 23, 180, 23).Contains(point));
    private void Pager(DrawingContext g)
    {
        if (PageCount <= 1) return;
        var x = DesiredWidgetSize.Width - 23; var y = DesiredWidgetSize.Height / 2 - 10;
        Round(g, new(x, y, 20, 20), 10, Tone("#E4E7ED", "#343945"));
        Txt(g, "›", x + 6, y - 1, 16, Muted, true);
    }
    public ProviderSurface(UsageMonitor monitor)
    {
        this.monitor = monitor;
        ReloadLogos();
    }
    internal void ReloadLogos()
    {
        foreach (var logo in logos.Values) logo.Dispose();
        logos.Clear();
        foreach (var p in monitor.Providers)
        {
            var icon = p.Id switch { "copilot" => "githubcopilot", "glm" or "glm-cn" => "zai", "minimax-cn" => "minimax", _ => p.Id };
            using var stream = AssetLoader.Open(new Uri($"avares://AIUsageWidget/Assets/{icon}.png"));
            using var original = new Bitmap(stream);
            var tinted = new WriteableBitmap(original.PixelSize, original.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var pixels = tinted.Lock())
            {
                original.CopyPixels(new PixelRect(original.PixelSize), pixels.Address, pixels.RowBytes * pixels.Size.Height, pixels.RowBytes);
                for (var y = 0; y < pixels.Size.Height; y++)
                    for (var x = 0; x < pixels.Size.Width; x++)
                    {
                        var offset = y * pixels.RowBytes + x * 4;
                        var alpha = Marshal.ReadByte(pixels.Address, offset + 3);
                        Marshal.WriteByte(pixels.Address, offset, (byte)(Main.B * alpha / 255));
                        Marshal.WriteByte(pixels.Address, offset + 1, (byte)(Main.G * alpha / 255));
                        Marshal.WriteByte(pixels.Address, offset + 2, (byte)(Main.R * alpha / 255));
                    }
            }
            logos[p.Id] = tinted;
        }
    }
    private static SolidColorBrush Brush(Color color) => new(color);
    private static void Round(DrawingContext g, Rect r, double radius, Color color, Color? border = null) =>
        g.DrawRectangle(Brush(color), border is { } c ? new Pen(Brush(c)) : null, r, radius, radius);
    private static FormattedText Text(string text, double size, Color color, bool bold = false) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Bold : Regular, size, Brush(color));
    private static void Txt(DrawingContext g, string text, double x, double y, double size, Color color, bool bold = false) => g.DrawText(Text(text, size, color, bold), new(x, y));
    public override void Render(DrawingContext g)
    {
        base.Render(g);
        if (monitor.Preferences.Island) { Island(g); Pager(g); return; }
        if (monitor.Preferences.Compact)
        {
            foreach (var (provider, index) in VisibleProviders.Select((p, i) => (p, i))) Badge(g, provider, index * 100);
            Pager(g);
            return;
        }
        Round(g, new(0.5, 0.5, 319, CardsHeight - 1), 20, Bg, Tone("#D9DDE5", "#414650"));
        Txt(g, "Usage", 18, 11, 24, Main, true);
        Round(g, new(100, 17, 60, 20), 10, Tone("#E4E7ED", "#343945"));
        Txt(g, monitor.Preferences.ShowUsed ? "% used" : "% left", 109, 20, 11, Muted, true);
        Txt(g, Updating ? "···" : "↻", 224, 10, 26, Muted);
        Txt(g, "—", 256, 12, 22, Muted);
        Txt(g, "⋮", 288, 10, 26, Muted);
        double top = 48;
        foreach (var p in VisibleProviders)
        {
            var state = monitor.States[p.Id];
            Round(g, new(12, top + 2, 296, p.CardHeight), 16, Tone("#E0E3E9", "#16181E"));
            Round(g, new(12, top, 296, p.CardHeight), 16, Card, Tone("#E9EBF0", "#414753"));
            Logo(g, p, new(24, top + 13, 17, 17));
            Txt(g, p.Name, 48, top + 12, 16, Main, true);
            var status = state.Stale ? "Stale · hover for details" : state.Reading == null ? Updating ? "Connecting" : "Unavailable" : "Live";
            var statusText = Text(status, 10, state.Stale ? Amber : Muted);
            g.DrawText(statusText, new(296 - statusText.Width, top + 16));
            for (var i = 0; i < p.QuotaLabels.Length; i++)
                Row(g, state.Reading?.Quotas.ElementAtOrDefault(i) ?? new(p.QuotaLabels[i], null, null), top + 41 + i * 51, p.Accent, state.Stale);
            Txt(g, state.Reading?.Balance ?? (state.Error != null ? "Unavailable · hover for details" : $"Connecting to {p.Name}…"), 24, top + p.CardHeight - 18, 11, Muted);
            if (p.HasFreeResets)
            {
                var resets = state.Reading?.FreeResets;
                Round(g, new(205, top + p.CardHeight - 22, 91, 17), 8, Tone("#EBF1FA", "#303E53"));
                Txt(g, resets is { } n ? $"Free resets  {n}" : "Free resets  —", 211, top + p.CardHeight - 19, 10, state.Stale ? Amber : Accent(p.Accent), true);
            }
            top += p.CardHeight + 8;
        }
        var names = string.Join(" + ", VisibleProviders.Select(p => p.Name));
        if (PageCount > 1) names = $"Page {Page + 1}/{PageCount} · scroll for more";
        if (Text(names, 10, Muted).Width > 172) names = $"{VisibleProviders.Length} providers";
        Txt(g, names, 18, CardsHeight - 17, 10, Muted);
        Txt(g, Notice != null ? "Settings error · hover" : Updating ? "Updating…" : "Updates every minute", 198, CardsHeight - 17, 10, Notice != null ? Amber : Muted);
    }
    private void Island(DrawingContext g)
    {
        if (monitor.Preferences.CompactIsland) { CompactIsland(g); return; }
        var center = IslandHeight / 2;
        Round(g, new(0.5, 0.5, DesiredWidgetSize.Width - 1, IslandHeight - 1), 8, Bg, Tone("#CCD2DD", "#454B58"));
        double x = 12;
        foreach (var p in VisibleProviders)
        {
            var state = monitor.States[p.Id];
            if (x > 12) g.DrawLine(new Pen(Brush(Tone("#E1E4EB", "#414753"))), new(x - 6, center - 11), new(x - 6, center + 11));
            Logo(g, p, new(x + 2, center - 12, 12, 12));
            Txt(g, p.Name, x + 19, center - 13, 10, Main, true);
            var status = state.Stale ? "STALE" : state.Reading == null ? Updating ? "Loading" : "No data" : monitor.Preferences.ShowUsed ? "% used" : "% left";
            Txt(g, status, x + 2, center + 3, 9, state.Stale || Notice != null ? Amber : Muted);
            for (var i = 0; i < p.QuotaLabels.Length; i++)
            {
                var q = state.Reading?.Quotas.ElementAtOrDefault(i);
                var value = q?.Used is { } n ? monitor.Preferences.ShowUsed ? n : 100 - n : (double?)null;
                var color = state.Stale ? Amber : q?.Used >= 90 ? Tone("#C14E4E", "#F18484") : q?.Used >= 75 ? Amber : Accent(p.Accent);
                var left = x + IslandNameWidth(p) + i * 64;
                Txt(g, Label(p.QuotaLabels[i]), left, center - 15, 9, Muted);
                Txt(g, value is { } v ? $"{v:0}%" : q?.Note == "Unlimited" ? "∞" : "—", left, center - 4, 14, state.Stale ? Amber : Main, true);
                Round(g, new(left, center + 14, 48, 2), 1, Tone("#E9EBF0", "#414753"));
                if (value is > 0) Round(g, new(left, center + 14, 48 * Math.Clamp(value.Value, 0, 100) / 100, 2), 1, color);
            }
            x += IslandProviderWidth(p);
        }
    }
    private void CompactIsland(DrawingContext g)
    {
        var center = IslandHeight / 2;
        Round(g, new(0.5, 0.5, DesiredWidgetSize.Width - 1, IslandHeight - 1), 8, Bg, Tone("#CCD2DD", "#454B58"));
        double x = 12;
        foreach (var p in VisibleProviders)
        {
            var state = monitor.States[p.Id];
            if (x > 12) g.DrawLine(new Pen(Brush(Tone("#E1E4EB", "#414753"))), new(x - 6, center - 8), new(x - 6, center + 8));
            Logo(g, p, new(x, center - 8, 16, 16));
            var left = x + 23;
            var quotas = CompactQuotas(p);
            foreach (var q in quotas)
            {
                var value = q.Used is { } n ? monitor.Preferences.ShowUsed ? n : 100 - n : (double?)null;
                var color = state.Stale || Notice != null ? Amber : Main;
                if (quotas.Length > 1) { Txt(g, Label(q.Label), left, center - 5, 9, Muted); left += 18; }
                Txt(g, value is { } v ? $"{v:0}%" : q?.Note == "Unlimited" ? "∞" : "—", left, center - 9, 13, color, true);
                if (state.Stale) g.DrawEllipse(Brush(Amber), null, new Point(left + 14, center + 11), 1.5, 1.5);
                left += 44;
            }
            x += DisplayIslandWidth(p);
        }
    }
    private void Logo(DrawingContext g, ProviderDefinition provider, Rect target) => g.DrawImage(logos[provider.Id], target);
    private void Row(DrawingContext g, Quota quota, double y, Color accent, bool stale)
    {
        Txt(g, quota.Label, 24, y, 12, Muted);
        var value = quota.Used is { } n ? monitor.Preferences.ShowUsed ? n : 100 - n : (double?)null;
        var color = stale ? Amber : quota.Used >= 90 ? Tone("#C14E4E", "#F18484") : quota.Used >= 75 ? Amber : Accent(accent);
        var label = Text(value is { } v ? $"{v:0}%" : quota.Note == "Unlimited" ? "∞" : "—", 20, value == null ? Muted : stale || quota.Used >= 75 ? color : Main, true);
        g.DrawText(label, new(296 - label.Width, y - 6));
        Round(g, new(24, y + 19, 272, 4), 2, Tone("#E9EBF0", "#414753"));
        if (value is > 0) Round(g, new(24, y + 19, 272 * value.Value / 100, 4), 2, color);
        Txt(g, Widget.ResetText(quota.Reset, DateTimeOffset.Now), 24, y + 27, 11, Muted);
    }
    private static string Label(string value) => value switch { "5 hours" => "5h", "Weekly" => "Wk", "Monthly" => "Mo", "Premium" => "Prem", "Models" => "Quota", _ => value };
    private void Badge(DrawingContext g, ProviderDefinition p, double x)
    {
        var state = monitor.States[p.Id];
        g.DrawEllipse(Brush(Tone("#24302534", "#60000000")), null, new Rect(x, 1, 96, 95));
        g.DrawEllipse(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = [new GradientStop(Tone("#FEFEFF", "#353A45"), 0), new GradientStop(Tone("#E8EBF1", "#242730"), 1)] }, new Pen(Brush(Tone("#DCFFFFFF", "#805A6272")), 0.8), new Rect(x + 2, 2, 92, 92));
        var quotas = state.Reading?.Quotas ?? p.QuotaLabels.Select(q => new Quota(q, null, null)).ToArray();
        for (var i = 0; i < Math.Min(2, quotas.Length); i++) Ring(g, quotas[i], x, 6 + i * 4, i == 0 ? Accent(p.Accent) : Tone("#99AAC4", "#9EAFCE"), state.Stale);
        var titleSize = p.Name.Length > 8 ? 8 : 10;
        var titleWidth = Text(p.Name, titleSize, Muted, true).Width;
        var titleX = x + (96 - titleWidth - 17) / 2;
        Logo(g, p, new(titleX, 19, 12, 12));
        Txt(g, p.Name, titleX + 17, 19, titleSize, Muted, true);
        string Percent(Quota? q) => q?.Used is { } n ? $"{(monitor.Preferences.ShowUsed ? n : 100 - n):0}%" : q?.Note == "Unlimited" ? "∞" : "—";
        void Center(string text, double y, double size, Color color, bool bold = false)
        {
            var ft = Text(text, size, color, bold); g.DrawText(ft, new(x + 48 - ft.Width / 2, y));
        }
        if (p.QuotaLabels.Length > 1)
        {
            Center($"{Label(p.QuotaLabels[0])}  {Percent(quotas.ElementAtOrDefault(0))}", 35, 14, Main, true);
            Center($"{Label(p.QuotaLabels[1])}  {Percent(quotas.ElementAtOrDefault(1))}", 53, 11, Muted);
            var free = p.HasFreeResets ? state.Reading?.FreeResets : null;
            Center(state.Stale ? "STALE" : free > 0 ? $"↻ {free} free" : monitor.Preferences.ShowUsed ? "used" : "left", 70, 9, state.Stale ? Amber : Muted);
        }
        else
        {
            Center(Percent(quotas.FirstOrDefault()), 34, 22, Main, true);
            Center(state.Stale ? "STALE" : $"{Label(p.QuotaLabels[0])} {(monitor.Preferences.ShowUsed ? "used" : "left")}", 62, 10, state.Stale ? Amber : Muted);
        }
    }
    private void Ring(DrawingContext g, Quota quota, double x, double inset, Color color, bool stale)
    {
        var rect = new Rect(x + inset, inset, 96 - inset * 2, 96 - inset * 2);
        g.DrawEllipse(null, new Pen(Brush(Tone("#DDE1E9", "#454C59")), 2), rect);
        if (quota.Used is not { } used) return;
        var value = monitor.Preferences.ShowUsed ? used : 100 - used;
        if (value <= 0) return;
        var pen = new Pen(Brush(stale ? Amber : used >= 90 ? Tone("#C14E4E", "#F18484") : color), 2, lineCap: PenLineCap.Round);
        if (value >= 100) { g.DrawEllipse(null, pen, rect); return; }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var angle = value / 100 * Math.PI * 2 - Math.PI / 2;
            context.BeginFigure(new(rect.Center.X, rect.Top), false);
            context.ArcTo(new(rect.Center.X + rect.Width / 2 * Math.Cos(angle), rect.Center.Y + rect.Height / 2 * Math.Sin(angle)), new Size(rect.Width / 2, rect.Height / 2), 0, value > 50, SweepDirection.Clockwise);
            context.EndFigure(false);
        }
        g.DrawGeometry(null, pen, geometry);
    }
    internal ProviderDefinition? ProviderAt(Point point)
    {
        if (monitor.Preferences.Island)
        {
            if (point.Y < 3 || point.Y > IslandHeight - 3) return null;
            double left = 12;
            foreach (var p in VisibleProviders)
            {
                if (point.X >= left && point.X < left + DisplayIslandWidth(p)) return p;
                left += DisplayIslandWidth(p);
            }
            return null;
        }
        if (monitor.Preferences.Compact)
        {
            var index = (int)(point.X / 100);
            if (index < 0 || index >= VisibleProviders.Length) return null;
            var dx = point.X - (index * 100 + 48); var dy = point.Y - 48;
            return dx * dx + dy * dy <= 48 * 48 ? VisibleProviders[index] : null;
        }
        double y = 48;
        foreach (var p in VisibleProviders)
        {
            if (point.X >= 12 && point.X <= 308 && point.Y >= y && point.Y < y + p.CardHeight) return p;
            y += p.CardHeight + 8;
        }
        return null;
    }
    public void Dispose() { foreach (var image in logos.Values) image.Dispose(); }
}
