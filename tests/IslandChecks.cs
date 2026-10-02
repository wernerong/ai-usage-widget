using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UsageWidget;

internal static class IslandChecks
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL island: " + message); checks++; }
        foreach (var layout in new[] { WidgetLayout.IslandBar, WidgetLayout.CompactIsland, WidgetLayout.AdaptiveIsland, WidgetLayout.SpotlightIsland })
        {
            var old = JsonSerializer.Deserialize<Preferences>($$"""{"Layout":"{{layout}}","X":123,"Y":47,"DarkMode":true,"AutomaticUpdates":false} """)!;
            Check(old.Layout == layout && old.IslandStyle == IslandStyle.Continuous && old.X == 123 && old.DarkMode && !old.AutomaticUpdates, "old settings retain layout and safely default style");
            foreach (var style in Enum.GetValues<IslandStyle>())
            {
                old.IslandStyle = style;
                var saved = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(old))!;
                Check(saved.Layout == layout && saved.IslandStyle == style && saved.Y == 47, "named layout/style round trip");
            }
            Check(layout.IsIsland(), "all island layouts share platform placement");
        }
        foreach (var invalid in new[] { "\"FutureStyle\"", "15", "null", "{}", "[]" })
            Check(JsonSerializer.Deserialize<Preferences>("{\"IslandStyle\":" + invalid + ",\"Opacity\":0.7,\"Layout\":\"CompactIsland\"}")
                is { IslandStyle: IslandStyle.Continuous, Opacity: .7, Layout: WidgetLayout.CompactIsland }, "unknown style does not discard other preferences");
        Check(WidgetLayouts.FromArguments(["--adaptive-island"], WidgetLayout.Focus) == WidgetLayout.AdaptiveIsland, "adaptive CLI");
        Check(WidgetLayouts.FromArguments(["--spotlight-island"], WidgetLayout.Focus) == WidgetLayout.SpotlightIsland, "spotlight CLI");
        Check(WidgetLayout.SpotlightIsland.PageSize() == 1 && WidgetLayout.AdaptiveIsland.PageSize() == 3, "new island page sizes");
        using var grok = new GrokProvider();
        var providers = Widget.CreateProviders(grok).Select(p => p with { Read = _ => Task.FromResult(Widget.Fixture(p)) }).ToArray();
        using var monitor = new UsageMonitor(new Preferences(), providers);
        var widget = new Widget(["--render-check"], monitor, createTray: false, persist: false);
        var surface = widget.Surface;
        byte[] Pixels(double scale = 1)
        {
            surface.Measure(new(widget.Width, widget.Height)); surface.Arrange(new(0, 0, widget.Width, widget.Height));
            using var bitmap = new RenderTargetBitmap(new((int)Math.Ceiling(widget.Width * scale), (int)Math.Ceiling(widget.Height * scale)), new(96 * scale, 96 * scale));
            bitmap.Render(surface);
            var size = bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4; var buffer = Marshal.AllocHGlobal(size);
            try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer, size, bitmap.PixelSize.Width * 4); var bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, size); return bytes; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        MenuItem Find(string label)
        {
            widget.RefreshContextMenu();
            return surface.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == label);
        }
        void SelectCount(int count)
        {
            widget.SetAdaptiveHover(null);
            foreach (var p in providers) widget.SetProvider(p, true);
            foreach (var p in providers.Skip(count)) widget.SetProvider(p, false);
            foreach (var p in providers) { monitor.States[p.Id].Reading = Widget.Fixture(p); monitor.States[p.Id].Error = null; }
            surface.ShowProvider(providers[0].Id); widget.SetPercentage(false);
        }
        try
        {
            widget.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var layout in WidgetLayouts.MenuOrder)
            {
                widget.SetLayout(layout);
                Check(Find("Island style").IsEnabled == layout.IsIsland(), "style enabled only for island layout " + layout);
                widget.RefreshNativeMenu();
                Check(widget.NativeMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Island style").IsEnabled == layout.IsIsland(), "native style availability");
            }
            foreach (var count in new[] { 1, 2, 3, 4, 13 })
            foreach (var layout in WidgetLayouts.MenuOrder.Where(l => l.IsIsland()))
            foreach (var style in Enum.GetValues<IslandStyle>())
            {
                SelectCount(count); widget.SetLayout(layout); widget.SetIslandStyle(style);
                var styleMenu = Find("Island style");
                Check(styleMenu.Items.OfType<MenuItem>().Count(i => i.IsChecked) == 1, "exclusive style menu");
                Check(styleMenu.Items.OfType<MenuItem>().Single(i => i.IsChecked).Header?.ToString() == (style == IslandStyle.Continuous ? "Continuous" : "Provider pills"), "checked style matches saved setting");
                var seen = new HashSet<string>();
                for (var page = 0; page < surface.PageCount; page++)
                {
                    var regions = surface.IslandProviderRegions;
                    Check(regions.Length == surface.VisibleProviders.Length && regions.Length <= layout.PageSize(), "regions match visible providers");
                    Check(widget.Width == surface.DesiredWidgetSize.Width && widget.Height == 40, "window uses shared island geometry");
                    foreach (var (p, bounds) in regions)
                    {
                        seen.Add(p.Id);
                        Check(surface.ProviderAt(bounds.Center) == p, "provider centre targets exact rendered provider");
                        Check(bounds.X >= 0 && bounds.Right <= widget.Width, "provider bounds fit");
                        Check(widget.Details(p).Contains("Last successful") && widget.Details(p).Contains("Resets"), "full detail tooltip retained");
                    }
                    if (style == IslandStyle.ProviderPills)
                        for (var i = 1; i < regions.Length; i++)
                            Check(surface.ProviderAt(new((regions[i-1].Bounds.Right + regions[i].Bounds.X) / 2, 20)) == null, "transparent pill gap has no provider");
                    if (surface.PageCount > 1)
                        Check(surface.PageDirectionAt(surface.IslandPagerBounds.Center) == 1 && surface.ProviderAt(surface.IslandPagerBounds.Center) == null, "pager never overlaps provider hit regions");
                    foreach (var dark in new[] { false, true })
                    foreach (var used in new[] { false, true })
                    {
                        widget.SetDarkMode(dark); widget.SetPercentage(used);
                        var pixels = Pixels();
                        Check(pixels[3] == 0 && pixels.Any(v => v != 0), "real render with transparent corners");
                    }
                    widget.ChangePage(1);
                }
                Check(seen.SetEquals(providers.Take(count).Select(p=>p.Id)), "all enabled providers reachable");
            }
            SelectCount(4); widget.SetLayout(WidgetLayout.AdaptiveIsland);
            foreach (var style in Enum.GetValues<IslandStyle>())
            {
                widget.SetIslandStyle(style); widget.Position = new(300, 50);
                var idleWidth = widget.Width; var idlePosition = widget.Position;
                var idleRegions = surface.IslandProviderRegions;
                for (var i = 0; i < 3; i++)
                {
                    widget.SetAdaptiveHover(null);
                    widget.MouseMove(surface.IslandProviderRegions[i].Bounds.Center);
                    Check(surface.HoveredIslandProviderId == providers[i].Id && widget.Width > idleWidth, "pointer expands provider " + i);
                    var expanded = surface.IslandProviderRegions;
                    for (var other = 0; other < 3; other++)
                    {
                        Check((expanded[other].Bounds.Width > idleRegions[other].Bounds.Width) == (other == i), "only hovered provider changes width");
                        Check(surface.ProviderAt(expanded[other].Bounds.Center) == providers[other], "hit testing follows expanded geometry");
                    }
                    Check(Math.Abs(widget.Position.X + widget.Width / 2 - idlePosition.X - idleWidth / 2) <= 1, "hover maintains horizontal centre");
                    Check(monitor.Preferences.X == idlePosition.X && monitor.Preferences.Y == idlePosition.Y, "temporary expansion does not overwrite saved position");
                    var expandedPosition = widget.Position;
                    for (var refresh = 0; refresh < 8; refresh++) widget.SetPercentage(refresh % 2 == 0);
                    Check(widget.Position == expandedPosition, "refreshes do not drift window position");
                    widget.SetAdaptiveHover(null);
                    Check(widget.Position == idlePosition && widget.Width == idleWidth, "collapse restores exact original position");
                }
                widget.SetAdaptiveHover(providers[0].Id);
                widget.MouseMove(new(-20, -20));
                Check(surface.HoveredIslandProviderId != null && widget.IslandCollapsePending, "pointer exit schedules rather than immediately collapsing");
                var frame = new DispatcherFrame();
                using (DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(300)))
                    Dispatcher.UIThread.PushFrame(frame);
                Check(surface.HoveredIslandProviderId == null, "pointer exit collapses after short delay");
                widget.SetAdaptiveHover(providers[0].Id);
                var dragPoint = surface.IslandProviderRegions[0].Bounds.Center;
                widget.MouseDown(dragPoint, MouseButton.Left);
                widget.MouseMove(new(dragPoint.X + 20, dragPoint.Y + 15));
                var beforeRefresh = widget.Width;
                var originalReading = monitor.States[providers[0].Id].Reading!;
                monitor.States[providers[0].Id].Reading = originalReading with { Quotas = [new("Weekly", 40, null)] };
                widget.SetPercentage(false);
                Check(widget.Width == beforeRefresh, "refresh defers island resize while dragging");
                widget.CollapseAdaptiveHover();
                Check(surface.HoveredIslandProviderId == providers[0].Id, "drag freezes hover and prevents timer collapse");
                widget.MouseUp(new(dragPoint.X + 20, dragPoint.Y + 15), MouseButton.Left);
                Check(widget.Width == surface.DesiredWidgetSize.Width, "drag release applies deferred geometry");
                monitor.States[providers[0].Id].Reading = originalReading;
                widget.SetPercentage(false);
                widget.SetAdaptiveHover(null);
                Check(widget.Position != idlePosition, "drag commits new collapsed position");
                widget.Position = new(10000, 50);
                var edge = widget.Position;
                for (var repeat = 0; repeat < 12; repeat++)
                {
                    widget.SetAdaptiveHover(providers[0].Id);
                    Check(widget.Position.X >= 0, "expanded island stays in screen bounds");
                    widget.SetAdaptiveHover(null);
                    Check(widget.Position == edge, "edge-clamped expansion does not accumulate drift");
                }
                widget.SetAdaptiveHover(providers[0].Id); widget.ChangePage(1);
                Check(surface.HoveredIslandProviderId == null && surface.VisibleProviders.Single() == providers[3], "paging clears transient hover");
                widget.ChangePage(-1);
            }
            SelectCount(4); widget.SetLayout(WidgetLayout.SpotlightIsland);
            widget.MouseWheel(surface.IslandProviderRegions[0].Bounds.Center, new(0,-1));
            Check(surface.VisibleProviders.Single() == providers[1], "spotlight wheel next");
            widget.MouseWheel(surface.IslandProviderRegions[0].Bounds.Center, new(0,1));
            Check(surface.VisibleProviders.Single() == providers[0], "spotlight wheel previous");
            widget.MouseDown(surface.IslandPagerBounds.Center, MouseButton.Left); widget.MouseUp(surface.IslandPagerBounds.Center, MouseButton.Left);
            Check(surface.VisibleProviders.Single() == providers[1], "spotlight pager click");
            widget.SetProvider(providers[0], false);
            Check(surface.VisibleProviders.Single() == providers[1], "disabling earlier provider preserves spotlight");
            widget.SetProvider(providers[1], false);
            Check(surface.VisibleProviders.Single() == providers[2], "disabled spotlight advances to valid provider");
            SelectCount(4);
            foreach (var layout in new[] {WidgetLayout.AdaptiveIsland, WidgetLayout.SpotlightIsland})
            foreach (var style in Enum.GetValues<IslandStyle>())
            {
                widget.SetLayout(layout); widget.SetIslandStyle(style);
                var state = monitor.States[providers[0].Id]; var fixture = Widget.Fixture(providers[0]);
                foreach (var reading in new Reading?[] {null, fixture, fixture with {Quotas=[new("Weekly",null,null)]},
                    fixture with {Quotas=[new("Weekly",null,null,"Unlimited")]}, fixture with {Quotas=[new("Weekly",double.NaN,null)]},
                    fixture with {Quotas=[new("5 hours",100,null),new("Weekly",0,null)]}, fixture with {Quotas=[]},
                    fixture with {Quotas=[new(new string('W',120),92,null)]}, fixture with {Fetched=DateTimeOffset.Now.AddMinutes(-10)}})
                {
                    state.Reading=reading; surface.Updating=reading==null; widget.SetPercentage(false);
                    foreach (var hover in new[] {false,true})
                    {
                        widget.SetAdaptiveHover(hover?providers[0].Id:null);
                        foreach (var scale in new[] {1d,1.25,1.5,2}) Check(Pixels(scale).Any(v=>v!=0), "quota states and DPI render safely");
                    }
                }
                state.Reading=fixture; surface.Updating=false; widget.SetAdaptiveHover(providers[0].Id);
                var normal=Pixels(); state.Error="Offline"; widget.SetPercentage(false);
                Check(!normal.SequenceEqual(Pixels()) && widget.Details(providers[0]).Contains("Offline"), "stale/error distinction and full tooltip");
                state.Error=null; widget.SetAdaptiveHover(null);
                var quota = new Quota("Weekly",92,null);
                var colour = surface.QuotaColor(providers[0].Accent,quota,false);
                widget.SetPercentage(true);
                Check(surface.PercentageText(quota)=="92%" && surface.QuotaColor(providers[0].Accent,quota,false)==colour, "used mode retains usage-based severity");
                widget.SetPercentage(false);
                Check(surface.PercentageText(quota)=="8%" && surface.QuotaColor(providers[0].Accent,quota,false)==colour, "remaining mode retains usage-based severity");
            }
            SelectCount(4); widget.SetLayout(WidgetLayout.AdaptiveIsland);
            foreach (var scale in new[] {1d,1.25,1.5,2})
            {
                surface.IslandWidthLimit=600; surface.SetIslandHover(providers[0].Id);
                var size=surface.DesiredWidgetSize;
                Check(size.Width<=600, "optional expanded content fits narrow display");
                var area=new PixelRect(-1200,25,1200,1000);
                var placed=ScreenPlacement.Constrain(new(0,0),size,[(area,scale)]);
                Check(placed.X>=area.X && placed.X+Math.Ceiling(size.Width*scale)<=area.Right && placed.Y>=area.Y, "scaled macOS placement avoids menu bar and display edge");
                var windows=Widget.PositionArea(new(0,0,1920,1080),new(0,0,1920,1032),WidgetLayout.AdaptiveIsland.IsIsland(),true);
                Check(windows.Bottom==1080, "new islands allow existing Windows taskbar placement");
                Check(ScreenPlacement.TaskbarHeight(48*scale,scale)==40, "taskbar height remains 40 logical pixels");
                surface.SetIslandHover(null); surface.IslandWidthLimit=double.PositiveInfinity;
            }
            foreach (var layout in WidgetLayouts.MenuOrder.Where(l=>l.IsIsland()))
            foreach (var style in Enum.GetValues<IslandStyle>())
            foreach (var dark in new[] {false,true})
            {
                widget.SetLayout(layout); widget.SetIslandStyle(style); widget.SetDarkMode(dark); widget.SetPercentage(false);
                var suffix=(style==IslandStyle.ProviderPills?"-pills":"")+(dark?"-dark":"");
                widget.SaveRender($"render-{layout.Id()}{suffix}.png");
                if(layout==WidgetLayout.AdaptiveIsland)
                {
                    widget.SetAdaptiveHover(providers[0].Id); widget.SaveRender($"render-adaptive-island-expanded{suffix}.png"); widget.SetAdaptiveHover(null);
                }
            }
            widget.SetLayout(WidgetLayout.AdaptiveIsland);
            var menuItem=Find("Island style").Items.OfType<MenuItem>().Single(i=>i.Header?.ToString()=="Continuous");
            menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Check(monitor.Preferences.IslandStyle==IslandStyle.Continuous, "context menu applies style");
            widget.SetAdaptiveHover(providers[0].Id);
            Find("Reset position").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Check(surface.HoveredIslandProviderId == null && monitor.Preferences.X == widget.Position.X
                && monitor.Preferences.Y == widget.Position.Y, "reset position clears transient hover and persists the new resting position");
            Console.WriteLine($"PASS: {checks} island checks (shared geometry, styles, hover delay, dragging, anchoring, paging, migration, states, DPI)");
        }
        finally {widget.PrepareExit();widget.Close();}
    }
}
