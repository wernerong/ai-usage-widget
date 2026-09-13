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
    internal static readonly Color Bg = Color.Parse("#F2F3F6"), Card = Color.Parse("#FDFDFE"), Main = Color.Parse("#1E2026"), Muted = Color.Parse("#717681"), Amber = Color.Parse("#AF6F1E");
    private static readonly Typeface Regular = new("Inter"), Bold = new("Inter", FontStyle.Normal, FontWeight.Bold);
    public bool Updating { get; set; }
    public string? Notice { get; set; }
    internal double IslandHeight { get; set; } = 40;
    public double CardsHeight => 63 + monitor.Enabled.Sum(p => p.CardHeight + 8);
    internal static double IslandProviderWidth(ProviderDefinition p) => 60 + 64 * p.QuotaLabels.Length;
    public Size DesiredWidgetSize => monitor.Preferences.Island ? new(24 + monitor.Enabled.Sum(IslandProviderWidth), IslandHeight) : monitor.Preferences.Compact ? new(monitor.Enabled.Length * 100 - 4, 96) : new(320, CardsHeight);
    public ProviderSurface(UsageMonitor monitor)
    {
        this.monitor = monitor;
        foreach (var p in monitor.Providers)
        {
            using var stream = AssetLoader.Open(new Uri($"avares://AIUsageWidget/Assets/{p.Id}.png"));
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
        if (monitor.Preferences.Island) { Island(g); return; }
        if (monitor.Preferences.Compact)
        {
            foreach (var (provider, index) in monitor.Enabled.Select((p, i) => (p, i))) Badge(g, provider, index * 100);
            return;
        }
        Round(g, new(0.5, 0.5, 319, CardsHeight - 1), 20, Bg, Color.Parse("#D9DDE5"));
        Txt(g, "Usage", 18, 11, 24, Main, true);
        Round(g, new(100, 17, 60, 20), 10, Color.Parse("#E4E7ED"));
        Txt(g, monitor.Preferences.ShowUsed ? "% used" : "% left", 109, 20, 11, Muted, true);
        Txt(g, Updating ? "···" : "↻", 224, 10, 26, Muted);
        Txt(g, "—", 256, 12, 22, Muted);
        Txt(g, "⋮", 288, 10, 26, Muted);
        double top = 48;
        foreach (var p in monitor.Enabled)
        {
            var state = monitor.States[p.Id];
            Round(g, new(12, top + 2, 296, p.CardHeight), 16, Color.Parse("#E0E3E9"));
            Round(g, new(12, top, 296, p.CardHeight), 16, Card, Color.Parse("#E9EBF0"));
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
                Round(g, new(205, top + p.CardHeight - 22, 91, 17), 8, Color.Parse("#EBF1FA"));
                Txt(g, resets is { } n ? $"Free resets  {n}" : "Free resets  —", 211, top + p.CardHeight - 19, 10, state.Stale ? Amber : p.Accent, true);
            }
            top += p.CardHeight + 8;
        }
        Txt(g, string.Join(" + ", monitor.Enabled.Select(p => p.Name)), 18, CardsHeight - 17, 10, Muted);
        Txt(g, Notice != null ? "Settings error · hover" : Updating ? "Updating…" : "Updates every minute", 198, CardsHeight - 17, 10, Notice != null ? Amber : Muted);
    }
    private void Island(DrawingContext g)
    {
        var center = IslandHeight / 2;
        Round(g, new(0.5, 0.5, DesiredWidgetSize.Width - 1, IslandHeight - 1), 8, Bg, Color.Parse("#CCD2DD"));
        double x = 12;
        foreach (var p in monitor.Enabled)
        {
            var state = monitor.States[p.Id];
            if (x > 12) g.DrawLine(new Pen(Brush(Color.Parse("#E1E4EB"))), new(x - 6, center - 11), new(x - 6, center + 11));
            Logo(g, p, new(x + 2, center - 12, 12, 12));
            Txt(g, p.Name, x + 19, center - 13, 10, Main, true);
            var status = state.Stale ? "STALE" : state.Reading == null ? Updating ? "Loading" : "No data" : monitor.Preferences.ShowUsed ? "% used" : "% left";
            Txt(g, status, x + 2, center + 3, 9, state.Stale || Notice != null ? Amber : Muted);
            for (var i = 0; i < p.QuotaLabels.Length; i++)
            {
                var q = state.Reading?.Quotas.ElementAtOrDefault(i);
                var value = q?.Used is { } n ? monitor.Preferences.ShowUsed ? n : 100 - n : (double?)null;
                var color = state.Stale ? Amber : q?.Used >= 90 ? Color.Parse("#C14E4E") : q?.Used >= 75 ? Amber : p.Accent;
                var left = x + 60 + i * 64;
                Txt(g, Label(p.QuotaLabels[i]), left, center - 15, 9, Muted);
                Txt(g, value is { } v ? $"{v:0.#}%" : "—", left, center - 4, 14, state.Stale ? Amber : Main, true);
                Round(g, new(left, center + 14, 48, 2), 1, Color.Parse("#E9EBF0"));
                if (value is > 0) Round(g, new(left, center + 14, 48 * Math.Clamp(value.Value, 0, 100) / 100, 2), 1, color);
            }
            x += IslandProviderWidth(p);
        }
    }
    private void Logo(DrawingContext g, ProviderDefinition provider, Rect target) => g.DrawImage(logos[provider.Id], target);
    private void Row(DrawingContext g, Quota quota, double y, Color accent, bool stale)
    {
        Txt(g, quota.Label, 24, y, 12, Muted);
        var value = quota.Used is { } n ? monitor.Preferences.ShowUsed ? n : 100 - n : (double?)null;
        var color = stale ? Amber : quota.Used >= 90 ? Color.Parse("#C14E4E") : quota.Used >= 75 ? Amber : accent;
        var label = Text(value is { } v ? $"{v:0.#}%" : "—", 20, value == null ? Muted : stale || quota.Used >= 75 ? color : Main, true);
        g.DrawText(label, new(296 - label.Width, y - 6));
        Round(g, new(24, y + 19, 272, 4), 2, Color.Parse("#E9EBF0"));
        if (value is > 0) Round(g, new(24, y + 19, 272 * value.Value / 100, 4), 2, color);
        Txt(g, Widget.ResetText(quota.Reset, DateTimeOffset.Now), 24, y + 27, 11, Muted);
    }
    private static string Label(string value) => value switch { "5 hours" => "5h", "Weekly" => "Wk", _ => value };
    private void Badge(DrawingContext g, ProviderDefinition p, double x)
    {
        var state = monitor.States[p.Id];
        g.DrawEllipse(Brush(Color.Parse("#24302534")), null, new Rect(x, 1, 96, 95));
        g.DrawEllipse(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = [new GradientStop(Color.Parse("#FEFEFF"), 0), new GradientStop(Color.Parse("#E8EBF1"), 1)] }, new Pen(Brush(Color.Parse("#DCFFFFFF")), 0.8), new Rect(x + 2, 2, 92, 92));
        var quotas = state.Reading?.Quotas ?? p.QuotaLabels.Select(q => new Quota(q, null, null)).ToArray();
        for (var i = 0; i < Math.Min(2, quotas.Length); i++) Ring(g, quotas[i], x, 6 + i * 4, i == 0 ? p.Accent : Color.Parse("#99AAC4"), state.Stale);
        Logo(g, p, new(x + 23, 19, 12, 12));
        Txt(g, p.Name, x + 39, 19, 10, Muted, true);
        string Percent(Quota? q) => q?.Used is { } n ? $"{(monitor.Preferences.ShowUsed ? n : 100 - n):0.#}%" : "—";
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
        g.DrawEllipse(null, new Pen(Brush(Color.Parse("#DDE1E9")), 2), rect);
        if (quota.Used is not { } used) return;
        var value = monitor.Preferences.ShowUsed ? used : 100 - used;
        if (value <= 0) return;
        var pen = new Pen(Brush(stale ? Amber : used >= 90 ? Color.Parse("#C14E4E") : color), 2, lineCap: PenLineCap.Round);
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
            foreach (var p in monitor.Enabled)
            {
                if (point.X >= left && point.X < left + IslandProviderWidth(p)) return p;
                left += IslandProviderWidth(p);
            }
            return null;
        }
        if (monitor.Preferences.Compact)
        {
            var index = (int)(point.X / 100);
            if (index < 0 || index >= monitor.Enabled.Length) return null;
            var dx = point.X - (index * 100 + 48); var dy = point.Y - 48;
            return dx * dx + dy * dy <= 48 * 48 ? monitor.Enabled[index] : null;
        }
        double y = 48;
        foreach (var p in monitor.Enabled)
        {
            if (point.X >= 12 && point.X <= 308 && point.Y >= y && point.Y < y + p.CardHeight) return p;
            y += p.CardHeight + 8;
        }
        return null;
    }
    public void Dispose() { foreach (var image in logos.Values) image.Dispose(); }
}
