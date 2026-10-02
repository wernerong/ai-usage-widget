using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UsageWidget;

internal static class LayoutChecks
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL layout: " + description);
            checks++;
        }
        foreach (var compact in new[] { false, true })
        foreach (var island in new[] { false, true })
        foreach (var compactIsland in new[] { false, true })
        {
            var json = JsonSerializer.Serialize(new { Compact = compact, Island = island, CompactIsland = compactIsland,
                X = -50, Y = 45, Opacity = .7, DarkMode = true, AutomaticUpdates = false, Providers = new { codex = false } });
            var p = JsonSerializer.Deserialize<Preferences>(json)!;
            var expected = island ? compactIsland ? WidgetLayout.CompactIsland : WidgetLayout.IslandBar : compact ? WidgetLayout.RoundBadges : WidgetLayout.DetailedCards;
            Check(p.Layout == expected, "legacy layout precedence: " + json);
            var saved = JsonSerializer.Serialize(p);
            using var doc = JsonDocument.Parse(saved);
            Check(doc.RootElement.GetProperty("Layout").GetString() == expected.ToString(), "write a single named layout");
            Check(!doc.RootElement.TryGetProperty("Compact", out _) && !doc.RootElement.TryGetProperty("Island", out _)
                && !doc.RootElement.TryGetProperty("CompactIsland", out _), "do not write legacy flags");
            var reread = JsonSerializer.Deserialize<Preferences>(saved)!;
            Check(reread.Layout == expected && reread.X == -50 && reread.Y == 45 && reread.Opacity == .7 && reread.DarkMode
                && !reread.AutomaticUpdates && !reread.Providers["codex"], "migration preserves unrelated settings");
        }
        Check(JsonSerializer.Deserialize<Preferences>("{}")!.Layout == WidgetLayout.DetailedCards, "legacy default is cards");
        foreach (var json in new[] { "{\"Layout\":\"Focus\",\"Island\":true}", "{\"Island\":true,\"Layout\":\"Focus\"}" })
            Check(JsonSerializer.Deserialize<Preferences>(json)!.Layout == WidgetLayout.Focus, "explicit layout wins regardless of key order");
        foreach (var value in new[] { "\"FutureLayout\"", "99", "null", "{}" })
            Check(JsonSerializer.Deserialize<Preferences>("{\"Layout\":" + value + ",\"Opacity\":0.7}") is { Layout: WidgetLayout.DetailedCards, Opacity: .7 }, "unknown layout preserves other settings");
        var flags = new[] { "--cards", "--compact", "--compact-list", "--grid", "--bars", "--focus", "--rail", "--island", "--compact-island", "--adaptive-island", "--spotlight-island" };
        foreach (var (flag, layout) in flags.Zip(Enum.GetValues<WidgetLayout>()))
            Check(WidgetLayouts.FromArguments([flag], WidgetLayout.DetailedCards) == layout, "CLI selects " + layout);

        using var grok = new GrokProvider();
        var providers = Widget.CreateProviders(grok).Select(p => p with { Read = _ => Task.FromResult(Widget.Fixture(p)) }).ToArray();
        using var monitor = new UsageMonitor(new Preferences(), providers);
        var widget = new Widget(["--render-check"], monitor, createTray: false, persist: false);
        byte[] Pixels(double scale = 1)
        {
            widget.Surface.Measure(new(widget.Width, widget.Height));
            widget.Surface.Arrange(new(0, 0, widget.Width, widget.Height));
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(widget.Width * scale), (int)Math.Ceiling(widget.Height * scale)), new(96 * scale, 96 * scale));
            bitmap.Render(widget.Surface);
            var stride = bitmap.PixelSize.Width * 4; var length = stride * bitmap.PixelSize.Height;
            var buffer = Marshal.AllocHGlobal(length);
            try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer, length, stride); var bytes = new byte[length]; Marshal.Copy(buffer, bytes, 0, length); return bytes; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        void SelectCount(int n)
        {
            foreach (var p in providers) widget.SetProvider(p, true);
            foreach (var p in providers.Skip(n)) widget.SetProvider(p, false);
            foreach (var p in providers) { monitor.States[p.Id].Reading = Widget.Fixture(p); monitor.States[p.Id].Error = null; }
            widget.Surface.ShowProvider(providers[0].Id);
            widget.SetPercentage(false);
        }
        try
        {
            widget.Show(); Dispatcher.UIThread.RunJobs();
            widget.RefreshContextMenu();
            foreach (var name in new[] { "Connections", PlatformServices.StartupLabel })
                Check(!widget.Surface.ContextMenu!.Items.OfType<MenuItem>().Single(i => (string?)i.Header == name).IsEnabled,
                    "non-persistent preview disables external side effects: " + name);
            foreach (var count in new[] { 1, 2, 3, 4, 6, 7, 13 })
            {
                SelectCount(count);
                foreach (var layout in WidgetLayouts.MenuOrder)
                {
                    widget.SetLayout(layout); widget.Surface.ShowProvider(providers[0].Id); widget.SetPercentage(false);
                    var surface = widget.Surface;
                    var expectedPages = (count + layout.PageSize() - 1) / layout.PageSize();
                    Check(surface.PageCount == expectedPages, $"{layout}/{count}: correct page count");
                    Check(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(monitor.Preferences))!.Layout == layout, "round trip " + layout);
                    widget.RefreshContextMenu(); widget.RefreshNativeMenu();
                    var menu = widget.Surface.ContextMenu!.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Layout");
                    Check(menu.Items.OfType<MenuItem>().Count(i => i.IsChecked) == 1 && menu.Items.OfType<MenuItem>().Single(i => i.IsChecked).Header?.ToString() == layout.Title(), "exclusive context menu " + layout);
                    var native = widget.NativeMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Layout").Menu!;
                    Check(native.Items.OfType<NativeMenuItem>().Single(i => i.IsChecked).Header == layout.Title(), "native menu " + layout);
                    var seen = new HashSet<string>();
                    for (var page = 0; page < expectedPages; page++)
                    {
                        Check(surface.VisibleProviders.Length is > 0 && surface.VisibleProviders.Length <= layout.PageSize(), "nonempty bounded page");
                        foreach (var p in surface.VisibleProviders) seen.Add(p.Id);
                        Check(widget.Width <= 1200 && widget.Height <= 600, "desktop-sized layout");
                        if (layout.IsAdditional())
                        {
                            var content = new Rect(0, 0, widget.Width, widget.Height);
                            foreach (var (provider, bounds) in surface.ProviderRegions)
                            {
                                Check(content.Contains(bounds.TopLeft) && content.Contains(bounds.BottomRight), "provider region fits window");
                                Check(surface.ProviderAt(bounds.Center) == provider, "painted provider matches hover target");
                                Check(widget.Details(provider).Contains(provider.Name) && widget.Details(provider).Contains("Last successful"), "reuse provider details");
                            }
                            Check(surface.ProviderAt(new(0, 0)) == null && surface.ProviderAt(new(10, widget.Height - 2)) == null, "chrome is not a provider hit target");
                            if (layout == WidgetLayout.TileGrid && surface.VisibleProviders.Length > 1)
                                Check(surface.ProviderAt(new(widget.Width / 2, 60)) == null, "grid gutter is not a provider");
                        }
                        foreach (var dark in new[] { false, true })
                        foreach (var used in new[] { false, true })
                        {
                            widget.SetDarkMode(dark); widget.SetPercentage(used);
                            var pixels = Pixels();
                            Check(pixels.Where((_, i) => i % 4 == 3).Any(a => a > 0), "actual pixels " + layout);
                            Check(pixels[3] == 0, "transparent corner " + layout);
                        }
                        widget.ChangePage(1);
                    }
                    Check(seen.SetEquals(providers.Take(count).Select(p => p.Id)) && surface.Page == 0, "paging reaches all selected providers and wraps");
                }
            }
            foreach (var layout in new[] { WidgetLayout.RoundBadges, WidgetLayout.DetailedCards })
            {
                SelectCount(2); widget.SetLayout(layout); widget.Position = new(40, 40);
                var oldSize = new Size(widget.Width, widget.Height);
                widget.SetProvider(providers[1], false);
                Check(widget.Position == new PixelPoint(40 + (int)(oldSize.Width - widget.Width), 40 + (int)(oldSize.Height - widget.Height)),
                    "legacy subscription changes preserve bottom-right anchor " + layout);
            }
            SelectCount(13);
            widget.SetLayout(WidgetLayout.StatusRail); widget.ChangePage(1);
            var anchor = widget.Surface.VisibleProviders[0];
            widget.SetLayout(WidgetLayout.Focus);
            Check(widget.Surface.VisibleProviders.Single() == anchor && widget.Surface.Page == 8, "rail to focus preserves viewed provider");
            widget.SetLayout(WidgetLayout.CompactList);
            Check(widget.Surface.VisibleProviders.Contains(anchor) && widget.Surface.Page == 1, "focus to list preserves provider page");
            widget.SetLayout(WidgetLayout.Focus);
            widget.Surface.ShowProvider(providers[^1].Id); widget.SetPercentage(false);
            widget.SetProvider(providers[0], false);
            Check(widget.Surface.VisibleProviders.Single() == providers[^1], "disabling an earlier provider keeps focus stable");
            widget.SetProvider(providers[^1], false);
            Check(widget.Surface.Page < widget.Surface.PageCount && widget.Surface.VisibleProviders.Length == 1, "removing focused provider clamps page");
            SelectCount(13);
            foreach (var layout in WidgetLayouts.MenuOrder.Where(l => l.IsAdditional()))
            {
                widget.SetLayout(layout); widget.Surface.ShowProvider(providers[0].Id); widget.SetPercentage(false);
                var surface = widget.Surface;
                var menuPoint = surface.HeaderButtonBounds(SurfaceAction.Menu).Center;
                Check(surface.HeaderActionAt(menuPoint) == SurfaceAction.Menu, "menu hit target " + layout);
                widget.MouseDown(menuPoint, MouseButton.Left); widget.MouseUp(menuPoint, MouseButton.Left);
                Check(surface.ContextMenu!.IsOpen, "new header opens existing settings menu"); surface.ContextMenu.Close();
                if (layout != WidgetLayout.StatusRail)
                {
                    var hidePoint = surface.HeaderButtonBounds(SurfaceAction.Hide).Center;
                    widget.MouseDown(hidePoint, MouseButton.Left); widget.MouseUp(hidePoint, MouseButton.Left);
                    Check(!widget.IsVisible, "header hide " + layout); widget.Restore();
                    monitor.States[providers[0].Id].Reading = null;
                    var refreshPoint = surface.HeaderButtonBounds(SurfaceAction.Refresh).Center;
                    widget.MouseDown(refreshPoint, MouseButton.Left); widget.MouseUp(refreshPoint, MouseButton.Left);
                    Check(monitor.States[providers[0].Id].Reading != null, "header refresh uses existing monitor " + layout);
                }
                monitor.States[providers[0].Id].Reading = null;
                widget.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F5 });
                Check(monitor.States[providers[0].Id].Reading != null, "F5 uses existing refresh " + layout);
                var page = surface.Page;
                widget.MouseDown(surface.NextPageBounds.Center, MouseButton.Left); widget.MouseUp(surface.NextPageBounds.Center, MouseButton.Left);
                Check(surface.Page == (page + 1) % surface.PageCount, "next page click " + layout);
                widget.MouseDown(surface.PreviousPageBounds.Center, MouseButton.Left); widget.MouseUp(surface.PreviousPageBounds.Center, MouseButton.Left);
                Check(surface.Page == page, "previous page click " + layout);
                widget.MouseWheel(new(40, 60), new(0, -1));
                Check(surface.Page == (page + 1) % surface.PageCount, "wheel navigation " + layout);
                widget.ChangePage(-1);
                widget.Position = new(40, 40);
                widget.MouseDown(new(15, 15), MouseButton.Left); widget.MouseMove(new(-1000, -1000));
                Check(widget.Position == new PixelPoint(0, 0), "drag clamps to screen " + layout);
                widget.MouseUp(new(15, 15), MouseButton.Left);
                widget.SetDarkMode(false); var light = Pixels(); widget.SetDarkMode(true);
                Check(!light.SequenceEqual(Pixels()), "theme changes actual pixels " + layout);
                var beforeSize = new Size(widget.Width, widget.Height); var beforePosition = widget.Position;
                monitor.Preferences.Opacity = .7; widget.SetLayout(layout);
                Check(surface.Opacity == .7 && beforeSize == new Size(widget.Width, widget.Height) && beforePosition == widget.Position, "70 percent opacity preserves geometry");
                monitor.Preferences.Opacity = 1; widget.SetLayout(layout);
                Check(widget.Topmost, "always-on-top remains enabled");
                var state = monitor.States[providers[0].Id];
                var complete = state.Reading!;
                foreach (var reading in new Reading?[] { null, complete with { Quotas = [new("Monthly", 0, null)] },
                    complete with { Quotas = [new("Weekly", null, null, "Unlimited")] }, complete with { Quotas = [new("Weekly", null, null)] },
                    complete with { Quotas = [new(new string('W', 160), 99, null)] }, complete with { Quotas = [] },
                    complete with { Fetched = DateTimeOffset.Now.AddMinutes(-10) } })
                {
                    state.Reading = reading; state.Error = null; surface.Updating = reading == null;
                    Check(Pixels().Any(b => b != 0), "missing, unlimited, unknown, long label and stale render " + layout);
                }
                state.Reading = complete; surface.Updating = false; widget.SetPercentage(false);
                var normal = Pixels(); state.Error = "Offline";
                Check(!normal.SequenceEqual(Pixels()) && widget.Details(providers[0]).Contains("Offline"), "stale display and details " + layout);
                state.Error = null;
                var quota = new Quota("Weekly", 92, null);
                Check(surface.DisplayPercentage(quota) == 8 && surface.PercentageText(quota) == "8%", "remaining calculation");
                var critical = surface.QuotaColor(providers[0].Accent, quota, false);
                widget.SetPercentage(true);
                Check(surface.DisplayPercentage(quota) == 92 && surface.QuotaColor(providers[0].Accent, quota, false) == critical, "severity is independent of display direction");
                Check(surface.PercentageText(new("Weekly", null, null)) == "—" && surface.PercentageText(new("Weekly", null, null, "Unlimited")) == "∞", "unknown and unlimited semantics");
                Check(surface.DisplayPercentage(new("Weekly", double.NaN, null)) == null, "nonfinite values stay unknown");
                Check(surface.DisplayPercentage(new("Weekly", 0, null, "Unlimited")) == null, "unlimited never invents a full progress bar");
                widget.SetPercentage(false);
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 }) Check(Pixels(scale).Any(b => b != 0), "mixed DPI rendering " + layout);
                foreach (var dark in new[] { false, true })
                {
                    widget.SetDarkMode(dark);
                    widget.SaveRender($"render-{layout.Id()}{(dark ? "-dark" : "")}.png");
                }
            }
            foreach (var width in new[] { 20d, 50, 100 })
            {
                var fitted = ProviderSurface.FitText("A long model quota label with a very long name", width, 11);
                var text = new FormattedText(fitted, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.Black);
                Check(text.Width <= width, "long labels fit without shrinking font");
            }
            foreach (var (width, size) in new[] { (40d, 13d), (37d, 13d), (36d, 12d), (35d, 12d) })
                Check(ProviderSurface.FitText("100%", width, size, true) == "100%", "largest percentage fits without truncation");
            Console.WriteLine($"PASS: {checks} additional layout checks (migration, eleven layouts, counts 1/2/3/4/6/7/13, pages, pointer controls, states, themes, DPI)");
        }
        finally { widget.PrepareExit(); widget.Close(); }
    }
}
