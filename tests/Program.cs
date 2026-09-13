using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            widget.RefreshContextMenu();
            IEnumerable<object?> items = widget.Surface.ContextMenu!.Items;
            MenuItem? item = null;
            foreach (var label in labels) { item = items.OfType<MenuItem>().Single(i => (string?)i.Header == label); items = item.Items; }
            return item!;
        }
        void Click(params string[] labels)
        {
            var item = Find(labels);
            widget.Surface.ContextMenu!.Open(widget.Surface);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check(widget.IsVisible, "Settings action leaves the widget visible: " + labels[^1]);
        }
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
            widget.Show();
            Dispatcher.UIThread.RunJobs();
            var originalContext = widget.Surface.ContextMenu;
            var originalNative = widget.NativeMenu;
            var originalNativeItem = originalNative.Items[0];
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
            Click("Layout", "Island bar");
            Check(widget.Width == 336 && widget.Height == 40, "Island fits both providers in a slim bar");
            Check(Find("Layout", "Island bar").IsChecked && !Find("Layout", "Round badges (compact)").IsChecked && !Find("Layout", "Detailed cards").IsChecked, "Island layout selection is exclusive");
            Check(widget.Surface.ProviderAt(new(30, 25)) == providers[0] && widget.Surface.ProviderAt(new(250, 25)) == providers[1], "Island tooltips target the correct providers");
            Check(ScreenPlacement.TaskbarHeight(96, 2) == 40, "Taskbar fitting accounts for 200 percent scaling");
            Check(ScreenPlacement.TaskbarHeight(40, 1) == 32, "Smaller taskbars retain even margins");
            Check(ScreenPlacement.AlignTaskbar(new(20,1548),40,2,new(0,1524,2880,96)) == new PixelPoint(20,1532), "Taskbar placement centers the island vertically");
            Check(ScreenPlacement.AlignTaskbar(new(20,500),40,2,new(0,1524,2880,96)) == new PixelPoint(20,500), "Free dragging away from the taskbar stays available");
            var size = new Size(336, 36);
            (PixelRect Area, double Scaling)[] pair = [(new(0, 0, 1920, 1080), 1), (new(1920, 0, 2560, 1440), 1.5)];
            Check(ScreenPlacement.Constrain(new(-100, -50), size, pair) == new PixelPoint(0, 0), "Dragging stops at outer top and left edges");
            Check(ScreenPlacement.Constrain(new(5000, 1500), size, pair) == new PixelPoint(3976, 1386), "Right and bottom limits include target monitor scaling");
            Check(ScreenPlacement.Constrain(new(2200, 500), size, pair) == new PixelPoint(2200, 500), "Dragging can enter a second monitor");
            Check(ScreenPlacement.Constrain(new(1800, 500), size, pair) == new PixelPoint(1920, 500), "Shared edge permits a jump onto the next display");
            Check(ScreenPlacement.Constrain(new(-1800, 50), size, [(new(-1920, 0, 1920, 1080), 1)]) == new PixelPoint(-1800, 50), "Negative monitor coordinates remain valid");
            Check(ScreenPlacement.Constrain(new(700, -500), size, [(new(0, -1080, 1920, 1080), 1)]) == new PixelPoint(700, -500), "Vertically stacked monitors work");
            Check(ScreenPlacement.Constrain(new(2000, 100), size, [(new(0,0,1920,1080),1), (new(1920,600,1920,1080),1)]) == new PixelPoint(1584,100), "Offset display gaps are not treated as screen space");
            Check(ScreenPlacement.Constrain(new(2200, 500), size, [pair[0]]) == new PixelPoint(1584, 500), "Disconnecting a display recovers an offscreen widget");
            Check(ScreenPlacement.Constrain(new(40, 0), size, [(new(0, 25, 1920, 1000), 1)]) == new PixelPoint(40, 25), "macOS usable area protects the menu bar");
            var desktop = new PixelRect(-1920, 0, 1920, 1080);
            var usable = new PixelRect(-1920, 0, 1920, 1032);
            Check(Widget.PositionArea(desktop, usable, true, true) == desktop, "Windows island can retain a taskbar position on a secondary monitor");
            Check(Widget.PositionArea(desktop, usable, false, true) == usable, "Other Windows layouts stay in the work area");
            Check(Widget.PositionArea(desktop, usable, true, false) == usable, "macOS island respects menu bar and Dock space");
            Click("Always on top");
            Check(!widget.Topmost && !monitor.Preferences.Pinned, "Always on top can be disabled for the island");
            Click("Always on top");
            Check(widget.Topmost && monitor.Preferences.Pinned, "Always on top can be restored for the island");
            widget.Position = new PixelPoint(40, 30);
            widget.MouseDown(new Point(20, 15), Avalonia.Input.MouseButton.Left);
            widget.MouseMove(new Point(-1000, -1000));
            Check(widget.Position == new PixelPoint(0, 0), "Captured pointer dragging clamps at the display edge");
            widget.MouseUp(new Point(20, 15), Avalonia.Input.MouseButton.Left);
            var released = widget.Position;
            widget.MouseMove(new Point(200, 100));
            Check(widget.Position == released, "Pointer release ends dragging");
            var islandLeft = Pixels();
            widget.SetPercentage(!monitor.Preferences.ShowUsed);
            Check(!islandLeft.SequenceEqual(Pixels()), "Island percentage mode changes rendered readings");
            monitor.States[providers[0].Id].Error = "Offline";
            var islandStale = Pixels();
            monitor.States[providers[0].Id].Error = null;
            Check(!islandStale.SequenceEqual(Pixels()), "Island exposes stale state visually");
            var dragged = new PixelPoint(40, 30);
            widget.Position = dragged;
            widget.Hide(); widget.Restore();
            Check(widget.Position == dragged, "Island restore preserves dragged position");
            var saved = System.Text.Json.JsonSerializer.Deserialize<Preferences>(System.Text.Json.JsonSerializer.Serialize(monitor.Preferences))!;
            Check(saved.Island && saved.X == dragged.X && saved.Y == dragged.Y, "Island choice and dragged position serialize");
            foreach (var only in providers)
            {
                foreach (var p in providers) widget.SetProvider(p, true);
                foreach (var p in providers.Where(p => p != only)) widget.SetProvider(p, false);
                Check(widget.Width == 24 + ProviderSurface.IslandProviderWidth(only) && widget.Height == 40, "Island shrinks for " + only.Name);
                Check(widget.Surface.ProviderAt(new(30, 25)) == only, "Single island provider hit testing");
                var state = monitor.States[only.Id];
                var reading = state.Reading;
                state.Reading = null;
                Check(Pixels().Any(b => b != 0), "Island renders missing readings safely");
                state.Reading = reading;
            }
            foreach (var p in providers) { widget.SetProvider(p, true); monitor.States[p.Id].Reading = Widget.Fixture(p); }
            widget.SetPercentage(false);
            widget.SaveRender("island-preview.png");
            Click("Layout", "Detailed cards");
            Check(!monitor.Preferences.Island, "Switching away clears island selection");
            Check(widget.Height == 346, "Both cards restore full height");
            Check(ReferenceEquals(originalContext, widget.Surface.ContextMenu) && ReferenceEquals(originalNative, widget.NativeMenu), "Menu objects survive all settings callbacks");
            Check(ReferenceEquals(originalNativeItem, widget.NativeMenu.Items[0]), "Native items are not replaced from settings callbacks");
            widget.RefreshNativeMenu();
            Check(widget.NativeMenu.Items.OfType<NativeMenuItem>().Any(i => i.Header == "Percentage display"), "Native items refresh on the next menu update");
            NativeMenuItem NativeFind(params string[] labels)
            {
                IEnumerable<NativeMenuItemBase> items = widget.NativeMenu.Items;
                NativeMenuItem? item = null;
                foreach (var label in labels) { item = items.OfType<NativeMenuItem>().Single(i => i.Header == label); items = item.Menu?.Items ?? []; }
                return item!;
            }
            var nativeRemaining = NativeFind("Percentage display", "Percentage remaining");
            var wasUsed = monitor.Preferences.ShowUsed;
            ((INativeMenuItemExporterEventsImplBridge)nativeRemaining).RaiseClicked();
            Check(monitor.Preferences.ShowUsed == wasUsed, "Native selection does not change settings inside the callback");
            Dispatcher.UIThread.RunJobs();
            Check(!monitor.Preferences.ShowUsed && widget.IsVisible, "Native percentage selection applies without hiding the widget");
            Check(ReferenceEquals(nativeRemaining, NativeFind("Percentage display", "Percentage remaining")), "Native selected item survives its callback and deferred action");
            widget.RefreshNativeMenu();
            ((INativeMenuItemExporterEventsImplBridge)NativeFind("Show / hide widget")).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Check(!widget.IsVisible, "Explicit native hide still works");
            ((INativeMenuItemExporterEventsImplBridge)NativeFind("Show / hide widget")).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Check(widget.IsVisible, "Explicit native restore still works");
            Console.WriteLine($"PASS: {count} UI checks (menus, sizing, hit testing, real pixels, transparency, stale state)");
            return 0;
        }
        finally { widget.PrepareExit(); widget.Close(); }
    }
}
