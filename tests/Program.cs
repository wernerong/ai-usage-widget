using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using UsageWidget;

internal static class UiChecks
{
    [STAThread]
    public static int Main()
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        using var grok = new GrokProvider();
        var providers = Widget.CreateProviders(grok).Select(p => p with { Read = _ => Task.FromResult(Widget.Fixture(p)) }).ToArray();
        using var monitor = new UsageMonitor(new Preferences(), providers);
        var widget = new Widget(["--render-check"], monitor, createTray: false, persist: false);
        MenuItem Find(params string[] labels)
        {
            IEnumerable<object?> items = widget.Surface.ContextMenu!.Items;
            MenuItem? item = null;
            foreach (var label in labels) { item = items.OfType<MenuItem>().Single(i => (string?)i.Header == label); items = item.Items; }
            return item!;
        }
        void Click(params string[] labels) => Find(labels).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        byte[] Pixels()
        {
            widget.Surface.Measure(new(widget.Width, widget.Height));
            widget.Surface.Arrange(new(0, 0, widget.Width, widget.Height));
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)widget.Width, (int)widget.Height), new(96, 96));
            bitmap.Render(widget.Surface);
            int stride = bitmap.PixelSize.Width * 4, length = stride * bitmap.PixelSize.Height;
            var buffer = Marshal.AllocHGlobal(length);
            try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer, length, stride); var pixels = new byte[length]; Marshal.Copy(buffer, pixels, 0, length); return pixels; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        try
        {
            foreach (var only in providers)
            {
                foreach (var p in providers) widget.SetProvider(p, true);
                foreach (var p in providers.Where(p => p != only)) Click("Subscriptions", p.Name);
                Check(monitor.Enabled.Single() == only, "Menu selects " + only.Name + " only");
                Check(!Find("Subscriptions", only.Name).IsEnabled, "Last provider stays enabled");
                Check(widget.Surface.ContextMenu!.Items.OfType<MenuItem>().Count(i => ((string?)i.Header)?.StartsWith("Open ") == true) == 1, "Usage links follow selection");
                monitor.States[only.Id].Reading = Widget.Fixture(only);
                foreach (var compact in new[] { false, true })
                {
                    Click("Layout", compact ? "Round badges (compact)" : "Detailed cards");
                    Check(widget.Width == (compact ? 96 : 320) && widget.Height == (compact ? 96 : 71 + only.CardHeight), "Single provider resizes both layouts");
                    Check(widget.Surface.ProviderAt(compact ? new Point(48, 48) : new Point(30, 75)) == only, "Tooltip hit testing follows provider order");
                    Check(widget.Surface.ProviderAt(new Point(0, 0)) == null, "Transparent corner has no provider hit target");
                    Click("Percentage display", "Percentage remaining");
                    var remaining = Pixels();
                    Click("Percentage display", "Percentage used");
                    Check(monitor.Preferences.ShowUsed && Find("Percentage display", "Percentage used").IsChecked && !Find("Percentage display", "Percentage remaining").IsChecked, "Percentage choices are exclusive");
                    var used = Pixels();
                    Check(!remaining.SequenceEqual(used), "Percentage selection changes actual rendered pixels");
                    Check(used[3] == 0 && used.Where((_, i) => i % 4 == 3).Any(a => a > 0 && a < 255), "Corners transparent and edges antialiased");
                    monitor.States[only.Id].Error = "Offline";
                    Check(widget.Details(only).Contains("Offline"), "Provider error remains available in tooltip");
                    Check(!used.SequenceEqual(Pixels()), "Stale data changes rendered appearance");
                    monitor.States[only.Id].Error = null;
                }
            }
            foreach (var p in providers) widget.SetProvider(p, true);
            Click("Layout", "Round badges (compact)");
            Check(widget.Width == 196 && widget.Surface.ProviderAt(new(148,48))?.Id == "grok", "Both-provider badge order and width");
            Click("Layout", "Detailed cards");
            Check(widget.Height == 346, "Both cards restore full height");
            Console.WriteLine($"PASS: {count} UI checks (menus, sizing, hit testing, real pixels, transparency, stale state)");
            return 0;
        }
        finally { widget.PrepareExit(); widget.Close(); }
    }
}
