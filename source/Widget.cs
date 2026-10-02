using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Styling;

namespace UsageWidget;

internal sealed class Widget : Window
{
    internal readonly UsageMonitor Monitor;
    internal readonly ProviderSurface Surface;
    private readonly GrokProvider grok = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer islandCollapseTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly TrayIcon? tray;
    private readonly bool persist;
    private readonly string[] args;
    private bool closing, initialized, clamping;
    private PixelPoint? dragOffset;
    private PixelPoint? islandHoverOrigin, lastIslandPointer;
    private double islandHoverCenter;
    internal bool IslandCollapsePending => islandCollapseTimer.IsEnabled;
    private bool changingLayout;
    private WindowsTaskbarOverlay? taskbarOverlay;
    private AutomaticUpdates? updates;
    private readonly PixelPoint? initialPosition;
    private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
    private string lastTip = "";
    private readonly ContextMenu contextMenu = new();
    internal readonly NativeMenu NativeMenu = new();
    private bool menusInitialized;
    private bool contextMenuDirty = true, nativeMenuDirty = true;
    internal static ProviderDefinition[] CreateProviders(GrokProvider grok) => [
        new("codex", "Codex", "https://chatgpt.com/codex/settings/usage", "live Codex account limits", Color.Parse("#3675C4"), ["5 hours", "Weekly"], true, true, CodexProvider.Read),
        new("grok", "Grok", "https://grok.com?_s=usage", "Grok CLI billing", Color.Parse("#8074AA"), ["Weekly"], false, true, grok.Read),
        new("grok-bot", "Grok Bot", "https://cursor.com/dashboard", "Grok Bot desktop weekly usage", Color.Parse("#568FAD"), ["Weekly"], false, false, GrokBotProvider.Read),
        new("claude", "Claude", "https://claude.ai/settings/usage", "Claude Code subscription login", Color.Parse("#B46C50"), ["5 hours", "Weekly"], false, false, ClaudeProvider.Read),
        new("cursor", "Cursor", "https://cursor.com/dashboard/usage", "Cursor CLI included billing-cycle allowance", Color.Parse("#6B7C91"), ["Monthly"], false, false, CursorProvider.Read),
        new("copilot", "Copilot", "https://github.com/settings/copilot", "GitHub Copilot monthly allowance", Color.Parse("#7374C4"), ["Premium", "Chat"], false, false, CopilotProvider.Read),
        new("gemini", "Gemini", "https://geminicli.com/docs/quota-and-pricing/", "Gemini CLI / Code Assist model quotas; not Gemini web chat", Color.Parse("#5084D5"), ["Models"], false, false, GeminiProvider.Read),
        new("antigravity", "Antigravity", "https://antigravity.google/docs/cli/commands/usage/", "Latest Antigravity CLI status-line quota; run /usage to refresh", Color.Parse("#5F8CA4"), ["Models"], false, false, AntigravityProvider.Read),
        new("kimi", "Kimi", "https://www.kimi.com/code/console", "Kimi Code subscription", Color.Parse("#5878D0"), ["5 hours", "Weekly"], false, false, ct => CodingPlanProviders.Read("kimi", ct)),
        new("glm", "GLM", "https://z.ai/manage-apikey/subscription", "Z.ai international Coding Plan", Color.Parse("#6581A6"), ["5 hours", "Weekly"], false, false, ct => CodingPlanProviders.Read("glm", ct)),
        new("glm-cn", "GLM CN", "https://bigmodel.cn", "Zhipu China Coding Plan", Color.Parse("#6581A6"), ["5 hours", "Weekly"], false, false, ct => CodingPlanProviders.Read("glm-cn", ct)),
        new("minimax", "MiniMax", "https://platform.minimax.io/subscribe/token-plan", "MiniMax international Token Plan", Color.Parse("#B26A89"), ["5 hours", "Weekly"], false, false, ct => CodingPlanProviders.Read("minimax", ct)),
        new("minimax-cn", "MiniMax CN", "https://platform.minimax.cn/subscribe/token-plan", "MiniMax China Token Plan", Color.Parse("#B26A89"), ["5 hours", "Weekly"], false, false, ct => CodingPlanProviders.Read("minimax-cn", ct))
    ];
    public Widget(string[] args, UsageMonitor? monitor = null, bool createTray = true, bool persist = true)
    {
        this.args = args;
        this.persist = persist && !args.Contains("--render") && !args.Contains("--render-check");
        var preferences = args.Contains("--render-check") ? new Preferences() : Preferences.Load();
        Monitor = monitor ?? new UsageMonitor(preferences, CreateProviders(grok));
        Monitor.Preferences.Layout = WidgetLayouts.FromArguments(args, Monitor.Preferences.Layout);
        if (args.Contains("--render-check"))
            foreach (var p in Monitor.Providers) Monitor.States[p.Id].Reading = Fixture(p);
        initialPosition = Monitor.Preferences.X is { } savedX && Monitor.Preferences.Y is { } savedY ? new PixelPoint(savedX, savedY) : null;
        Title = "AI Usage Widget";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = Monitor.Preferences.Pinned;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Surface = new ProviderSurface(Monitor);
        Content = Surface;
        ApplyTheme();
        AutomationProperties.SetName(Surface, "AI Usage Widget");
        var windowIcon = OperatingSystem.IsWindows() ? "widget.ico" : "widget.png";
        using (var stream = AssetLoader.Open(new Uri($"avares://AIUsageWidget/Assets/{windowIcon}"))) Icon = new WindowIcon(stream);
        if (createTray)
        {
            // Status icons have a separate small-size asset; a 1024px app icon
            // should never be resampled all the way down to the menu bar.
            var trayAsset = OperatingSystem.IsWindows() ? "tray.ico" : "tray.png";
            using var stream = AssetLoader.Open(new Uri($"avares://AIUsageWidget/Assets/{trayAsset}"));
            tray = new TrayIcon { Icon = new WindowIcon(stream), ToolTipText = "AI Usage Widget", IsVisible = true };
            // On macOS the status item opens its native menu. A click must not also hide the widget.
            if (!OperatingSystem.IsMacOS()) tray.Clicked += (_, _) => ToggleVisible();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
        }
        Monitor.Changed += UpdateDisplay;
        ApplyLayout(false);
        BuildMenus();
        PositionChanged += (_, _) => { if (initialized) ClampPosition(); SavePosition(); taskbarOverlay?.EnsureAboveTaskbar(); };
        Closing += (_, e) => { if (!closing) { e.Cancel = true; SetAdaptiveHover(null); Hide(); WriteHealth(); } };
        Screens.Changed += OnScreensChanged;
        ScalingChanged += (_, _) => { if (initialized) { SetAdaptiveHover(null); ClampPosition(); } };
        Closed += (_, _) => { Screens.Changed -= OnScreensChanged; Surface.Dispose(); };
        Opened += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            if (initialPosition is { } savedPosition) Position = savedPosition; else ResetPosition();
            ClampPosition();
            SavePosition();
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.HandleDescriptor == "HWND")
                taskbarOverlay = new WindowsTaskbarOverlay(this, () => !closing && Monitor.Preferences.Layout.IsIsland());
            taskbarOverlay?.EnsureAboveTaskbar();
            // Injected monitors are driven by the caller (including UI regression tests).
            if (monitor != null) return;
            if (args.Contains("--render-check")) { await RenderChecks(); return; }
            if (persist && !args.Contains("--render"))
            {
                var backend = new GithubWidgetUpdates();
                if (backend.Available) AntigravityProvider.MigrateInstalledCommand();
                if (backend.Available)
                {
                    try { PlatformServices.MigrateStartupForUpdates(); }
                    catch { Surface.Notice = "Could not update startup settings; toggle Start at login to retry."; }
                }
                updates = new AutomaticUpdates(backend, () => Monitor.Preferences.AutomaticUpdates,
                    () => !closing && dragOffset == null && !contextMenu.IsOpen && OwnedWindows.Count == 0,
                    () => { backend.Apply(!IsVisible); Quit(); });
                updates.Changed += BuildMenus;
                BuildMenus();
                _ = updates.Run();
                if (args.Contains("--hidden")) Hide();
            }
            timer.Start();
            await RefreshUsage();
            if (args.Contains("--render")) { SaveRender("widget-preview.png"); Quit(); }
        };
        timer.Tick += async (_, _) =>
        {
            UpdateDisplay();
            if (!Monitor.Busy && DateTimeOffset.Now >= nextRefresh) await RefreshUsage();
        };
        Surface.PointerPressed += OnSurfacePressed;
        islandCollapseTimer.Tick += (_, _) => CollapseAdaptiveHover();
        Surface.PointerExited += (_, _) => ScheduleIslandCollapse();
        Surface.PointerWheelChanged += (_, e) => { if (Surface.PageCount > 1) { ChangePage(e.Delta.Y < 0 ? 1 : -1); e.Handled = true; } };
        Surface.PointerMoved += (_, e) =>
        {
            if (dragOffset is { } offset)
            {
                var pointer = Surface.PointToScreen(e.GetPosition(Surface));
                Position = ConstrainPosition(new(pointer.X - offset.X, pointer.Y - offset.Y));
                e.Handled = true;
            }
            else
            {
                var point = e.GetPosition(Surface);
                var screenPoint = Surface.PointToScreen(point);
                // A resize can synthesize pointer events without actual mouse movement.
                if (screenPoint != lastIslandPointer)
                {
                    lastIslandPointer = screenPoint;
                    UpdateIslandHover(point);
                }
                UpdateTooltip(Surface.PointToClient(screenPoint));
            }
        };
        Surface.PointerReleased += (_, e) => { EndDrag(); e.Pointer.Capture(null); UpdateIslandHover(e.GetPosition(Surface)); };
        Surface.PointerCaptureLost += (_, _) => EndDrag();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { SetAdaptiveHover(null); Hide(); WriteHealth(); e.Handled = true; }
            else if (e.Key == Key.F5) { await RefreshUsage(); e.Handled = true; }
            else if (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { Surface.ContextMenu?.Open(Surface); e.Handled = true; }
        };
    }
    private record MenuChoice(string Label, Action? Action = null, bool? Checked = null, bool Enabled = true, MenuChoice[]? Children = null, bool Radio = false);
    private MenuChoice[] Choices()
    {
        var prefs = Monitor.Preferences;
        var choices = new List<MenuChoice> {
            new("Show / hide widget", ToggleVisible), new("Refresh now", () => _ = RefreshUsage()),
            new("Subscriptions", Children: Monitor.Providers.Select(p => new MenuChoice(p.Name, () => SetProvider(p, !prefs.IsEnabled(p)), prefs.IsEnabled(p), !prefs.IsEnabled(p) || Monitor.Enabled.Length > 1)).ToArray()),
            new("Connections", Enabled: persist, Children: Monitor.Providers.Where(p => ConnectionDialog.Supports(p.Id)).Select(p => new MenuChoice(p.Name, () => _ = ConnectProvider(p))).ToArray()),
            new($"Provider page {Surface.Page + 1}/{Surface.PageCount}", Children: [new("Previous providers", () => ChangePage(-1), Enabled: Surface.PageCount > 1), new("Next providers", () => ChangePage(1), Enabled: Surface.PageCount > 1)]),
            new("Layout", Children: WidgetLayouts.MenuOrder.SelectMany(layout =>
                (layout == WidgetLayout.AdaptiveIsland ? new[] { new MenuChoice("-") } : []).Append(
                    new MenuChoice(layout.Title(), () => SetLayout(layout), prefs.Layout == layout, Radio: true))).ToArray()),
            new("Island style", Enabled: prefs.Layout.IsIsland(), Children: [
                new("Continuous", () => SetIslandStyle(IslandStyle.Continuous), prefs.IslandStyle == IslandStyle.Continuous, Radio: true),
                new("Provider pills", () => SetIslandStyle(IslandStyle.ProviderPills), prefs.IslandStyle == IslandStyle.ProviderPills, Radio: true)]),
            new("Dark mode", () => SetDarkMode(!prefs.DarkMode), prefs.DarkMode),
            new("Always on top", () => { prefs.Pinned = Topmost = !prefs.Pinned; taskbarOverlay?.EnsureAboveTaskbar(); SavePreferences(); BuildMenus(); }, prefs.Pinned),
            new("Percentage display", Children: [new("Percentage remaining", () => SetPercentage(false), !prefs.ShowUsed, Radio: true), new("Percentage used", () => SetPercentage(true), prefs.ShowUsed, Radio: true)]),
            new("Opacity", Children: new[] {100,97,85,70}.Select(n => new MenuChoice($"{n}%", () => { prefs.Opacity = n / 100.0; ApplyOpacity(); SavePreferences(); BuildMenus(); }, Math.Abs(prefs.Opacity - n / 100.0) < 0.001, Radio: true)).ToArray()),
            new(PlatformServices.StartupLabel, () => {
                try { PlatformServices.SetStartup(!PlatformServices.StartupEnabled()); Surface.Notice = null; }
                catch (Exception ex) { Surface.Notice = "Could not change startup: " + ex.Message; _ = ShowError(Surface.Notice); }
                BuildMenus(); UpdateDisplay();
            }, PlatformServices.StartupEnabled(), Enabled: persist),
            new("-")
        };
        choices.AddRange(Monitor.Enabled.Select(p => new MenuChoice($"Open {p.Name} usage", () => {
            try { PlatformServices.OpenUrl(p.UsageUrl); } catch (Exception ex) { _ = ShowError("Could not open the browser: " + ex.Message); }
        }, Enabled: persist)));
        choices.AddRange([new("Reset position", () => { ResetPosition(); Restore(); }), new("-"),
            new("Updates", Children: [
                new("Automatic updates", () => { prefs.AutomaticUpdates = !prefs.AutomaticUpdates; SavePreferences(); BuildMenus(); }, prefs.AutomaticUpdates),
                new("Check for updates", () => { if (updates != null) _ = updates.Check(manual: true); }, Enabled: updates?.Available == true),
                new(updates?.Status ?? "Install the latest release to enable updates", Enabled: false)]),
            new($"AI Usage Widget v{Program.AppVersion}", Enabled: false), new("Exit", Quit)]);
        return choices.ToArray();
    }
    internal void BuildMenus()
    {
        // Keep menu objects alive for the entire window lifetime. Cocoa may still be
        // tracking a clicked item until its native callback has returned.
        contextMenuDirty = nativeMenuDirty = true;
        if (menusInitialized) return;
        menusInitialized = true;
        contextMenu.Opening += (_, _) => RefreshContextMenu();
        NativeMenu.NeedsUpdate += (_, _) => RefreshNativeMenu();
        RefreshContextMenu();
        RefreshNativeMenu();
        Surface.ContextMenu = contextMenu;
        if (tray != null) tray.Menu = NativeMenu;
    }
    internal void RefreshContextMenu()
    {
        if (!contextMenuDirty || contextMenu.IsOpen) return;
        contextMenu.Items.Clear();
        foreach (var choice in Choices()) contextMenu.Items.Add(ContextItem(choice));
        contextMenuDirty = false;
    }
    internal void RefreshNativeMenu()
    {
        // Called by NeedsUpdate, the supported native-menu mutation point.
        if (!nativeMenuDirty) return;
        NativeMenu.Items.Clear();
        foreach (var choice in Choices()) NativeMenu.Items.Add(NativeItem(choice));
        nativeMenuDirty = false;
    }
    private void QueueMenuAction(Action action)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!closing) action();
        }, DispatcherPriority.Background);
    }
    private Control ContextItem(MenuChoice choice)
    {
        if (choice.Label == "-") return new Separator();
        var item = new MenuItem { Header = choice.Label, IsEnabled = choice.Enabled, IsChecked = choice.Checked ?? false,
            ToggleType = choice.Checked == null ? MenuItemToggleType.None : choice.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox };
        if (choice.Children != null) foreach (var child in choice.Children) item.Items.Add(ContextItem(child));
        // Dismiss only the popup, then run the action outside its routed/native callback.
        if (choice.Action != null) item.Click += (_, e) =>
        {
            e.Handled = true;
            contextMenu.Close();
            QueueMenuAction(choice.Action);
        };
        return item;
    }
    private NativeMenuItemBase NativeItem(MenuChoice choice)
    {
        if (choice.Label == "-") return new NativeMenuItemSeparator();
        var item = new NativeMenuItem(choice.Label) { IsEnabled = choice.Enabled, IsChecked = choice.Checked ?? false,
            ToggleType = choice.Checked == null ? NativeMenuItemToggleType.None : choice.Radio ? NativeMenuItemToggleType.Radio : NativeMenuItemToggleType.CheckBox };
        if (choice.Children != null) { item.Menu = new(); foreach (var child in choice.Children) item.Menu.Items.Add(NativeItem(child)); }
        if (choice.Action != null) item.Click += (_, _) => QueueMenuAction(choice.Action);
        return item;
    }
    private async Task ShowError(string message)
    {
        var dialog = new Window { RequestedThemeVariant = RequestedThemeVariant, Title = "AI Usage Widget", Width = 380, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close } };
        Restore();
        await dialog.ShowDialog(this);
    }
    internal void ChangePage(int direction)
    {
        SetAdaptiveHover(null);
        Surface.ChangePage(direction); ApplyLayout(true); BuildMenus(); UpdateDisplay();
    }
    private async Task ConnectProvider(ProviderDefinition provider)
    {
        var dialog = new ConnectionDialog(provider);
        if (await dialog.ShowDialog<bool>(this))
        {
            Monitor.Reconnect(provider); SetProvider(provider, true); await RefreshUsage();
        }
    }
    internal void SetProvider(ProviderDefinition provider, bool enabled)
    {
        SetAdaptiveHover(null);
        var anchor = Monitor.Preferences.Layout.IsAdditional() || Monitor.Preferences.Layout.IsIsland() ? Surface.VisibleProviders.FirstOrDefault()?.Id : null;
        bool changed;
        changingLayout = true;
        try { changed = Monitor.SetEnabled(provider, enabled); }
        finally { changingLayout = false; }
        if (!changed) return;
        Surface.ShowProvider(anchor);
        ApplyLayout(true); SavePreferences(); BuildMenus();
        if (!args.Contains("--render-check")) _ = RefreshUsage();
    }
    private void ApplyTheme()
    {
        RequestedThemeVariant = Monitor.Preferences.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        if (Application.Current != null) Application.Current.RequestedThemeVariant = RequestedThemeVariant;
    }
    internal void SetDarkMode(bool dark)
    {
        Monitor.Preferences.DarkMode = dark;
        ApplyTheme(); Surface.ReloadLogos(); SavePreferences(); BuildMenus(); UpdateDisplay();
    }
    internal void SetPercentage(bool used) { Monitor.Preferences.ShowUsed = used; SavePreferences(); BuildMenus(); UpdateDisplay(); }
    internal void SetCompact(bool compact) => SetLayout(compact ? WidgetLayout.RoundBadges : WidgetLayout.DetailedCards);
    internal void SetIsland(bool compact = false) => SetLayout(compact ? WidgetLayout.CompactIsland : WidgetLayout.IslandBar);
    internal void SetIslandStyle(IslandStyle style)
    {
        SetAdaptiveHover(null);
        Monitor.Preferences.IslandStyle = style;
        if (Monitor.Preferences.Layout.IsIsland()) ApplyLayout(true);
        SavePreferences(); BuildMenus(); UpdateDisplay();
    }
    internal void SetLayout(WidgetLayout layout)
    {
        SetAdaptiveHover(null);
        var anchor = Surface.VisibleProviders.FirstOrDefault()?.Id;
        Monitor.Preferences.Layout = layout;
        Surface.ShowProvider(anchor);
        ApplyLayout(!layout.IsIsland() && !layout.IsAdditional());
        SavePreferences(); BuildMenus(); UpdateDisplay();
    }
    private void ApplyOpacity() => Surface.Opacity = Math.Clamp(Monitor.Preferences.Opacity, 0.5, 1);
    private void ApplyLayout(bool keepBottomRight)
    {
        UpdateIslandWidthLimit();
        var oldWidth = Width; var oldHeight = Height;
        var size = Surface.DesiredWidgetSize;
        Width = Surface.Width = size.Width; Height = Surface.Height = size.Height;
        ApplyOpacity();
        if (keepBottomRight && !Monitor.Preferences.Layout.IsAdditional() && double.IsFinite(oldWidth) && double.IsFinite(oldHeight))
            Position = Monitor.Preferences.Layout.IsIsland()
                ? new(Position.X + (int)((oldWidth - Width) * RenderScaling / 2), Position.Y)
                : new(Position.X + (int)((oldWidth - Width) * RenderScaling), Position.Y + (int)((oldHeight - Height) * RenderScaling));
        ClampPosition();
        lastTip = ""; ToolTip.SetTip(Surface, null);
    }
    private async void OnSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(Surface);
        if (Surface.PageDirectionAt(p) is var direction && direction != 0) { ChangePage(direction); e.Handled = true; return; }
        if (Monitor.Preferences.Layout.IsAdditional())
        {
            switch (Surface.HeaderActionAt(p))
            {
                case SurfaceAction.Menu: Surface.ContextMenu?.Open(Surface); break;
                case SurfaceAction.Hide: Hide(); WriteHealth(); break;
                case SurfaceAction.Refresh: await RefreshUsage(); break;
                default: StartDrag(e); break;
            }
            e.Handled = true;
        }
        else if (Monitor.Preferences.Layout.IsIsland()) { StartDrag(e); }
        else if (Monitor.Preferences.Layout == WidgetLayout.RoundBadges)
        {
            if (Surface.ProviderAt(p) == null) return;
            if (e.ClickCount == 2) SetCompact(false); else StartDrag(e);
        }
        else if (p.Y < 44)
        {
            if (p.X >= 282) Surface.ContextMenu?.Open(Surface);
            else if (p.X >= 248) { Hide(); WriteHealth(); }
            else if (p.X >= 215) await RefreshUsage();
            else StartDrag(e);
        }
    }
    private void StartDrag(PointerPressedEventArgs e)
    {
        islandCollapseTimer.Stop();
        var pointer = Surface.PointToScreen(e.GetPosition(Surface));
        dragOffset = new(pointer.X - Position.X, pointer.Y - Position.Y);
        e.Pointer.Capture(Surface);
        ToolTip.SetIsOpen(Surface, false);
        e.Handled = true;
    }
    private void EndDrag()
    {
        var wasDragging = dragOffset != null;
        dragOffset = null; lastIslandPointer = null;
        if (wasDragging && Monitor.Preferences.Layout.IsIsland()) ApplyLayout(true);
        if (wasDragging && islandHoverOrigin != null)
        {
            islandHoverCenter = Position.X + Width * RenderScaling / 2;
            islandHoverOrigin = new((int)Math.Round(islandHoverCenter - Surface.CollapsedIslandSize.Width * RenderScaling / 2), Position.Y);
        }
        ClampPosition(); SavePosition();
    }
    private void UpdateIslandWidthLimit()
    {
        if (!Monitor.Preferences.Layout.IsIsland()) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        Surface.IslandWidthLimit = screen == null ? double.PositiveInfinity
            : PositionArea(screen.Bounds, screen.WorkingArea, true, OperatingSystem.IsWindows()).Width / screen.Scaling;
    }
    private void UpdateIslandHover(Point point)
    {
        if (dragOffset != null || Monitor.Preferences.Layout != WidgetLayout.AdaptiveIsland) return;
        if (Surface.ProviderAt(point) is { } provider) { islandCollapseTimer.Stop(); SetAdaptiveHover(provider.Id); }
        else ScheduleIslandCollapse();
    }
    private void ScheduleIslandCollapse()
    {
        if (dragOffset == null && Surface.HoveredIslandProviderId != null && !islandCollapseTimer.IsEnabled) islandCollapseTimer.Start();
    }
    internal void CollapseAdaptiveHover()
    {
        if (dragOffset != null || contextMenu.IsOpen) return;
        SetAdaptiveHover(null);
    }
    internal void SetAdaptiveHover(string? id)
    {
        if (dragOffset != null) return;
        islandCollapseTimer.Stop();
        var origin = islandHoverOrigin;
        if (!Surface.SetIslandHover(id)) return;
        if (Surface.HoveredIslandProviderId != null)
        {
            if (origin == null)
            {
                islandHoverOrigin = Position;
                islandHoverCenter = Position.X + Width * RenderScaling / 2;
            }
            ApplyLayout(false);
            Position = ConstrainPosition(new((int)Math.Round(islandHoverCenter - Width * RenderScaling / 2), islandHoverOrigin!.Value.Y));
        }
        else
        {
            ApplyLayout(false);
            if (origin != null) Position = ConstrainPosition(origin.Value);
            islandHoverOrigin = null;
        }
        Surface.InvalidateVisual(); SavePosition(); WriteHealth();
    }
    private void UpdateTooltip(Point point)
    {
        if (Surface.PageDirectionAt(point) is var direction && direction != 0) { ToolTip.SetTip(Surface, $"{(direction < 0 ? "Previous" : "Next")} providers · page {Surface.Page + 1}/{Surface.PageCount} · scroll to switch pages"); lastTip = ""; return; }
        var p = Surface.ProviderAt(point);
        var text = p != null ? Details(p) : Surface.Notice ?? "Drag to move · right-click for settings";
        if (Monitor.Preferences.Layout.IsAdditional()) text = Surface.HeaderActionAt(point) switch
        {
            SurfaceAction.Menu => "Settings and Exit", SurfaceAction.Hide => "Hide to tray / menu bar",
            SurfaceAction.Refresh => "Refresh now", _ => text
        };
        if (Monitor.Preferences.Layout == WidgetLayout.DetailedCards && point.Y < 44)
            text = point.X >= 282 ? "Settings and Exit" : point.X >= 248 ? "Hide to tray / menu bar" : point.X >= 215 ? "Refresh now" : "Drag to move";
        if (Monitor.Preferences.Layout.IsIsland() || Monitor.Preferences.Layout.IsAdditional()) text += "\n\nDrag to move · right-click for settings";
        else if (Monitor.Preferences.Layout == WidgetLayout.RoundBadges) text += "\n\nDrag to move · double-click for cards · right-click for settings";
        if (lastTip != text) { lastTip = text; ToolTip.SetTip(Surface, text); }
    }
    internal string Details(ProviderDefinition provider)
    {
        var state = Monitor.States[provider.Id];
        var lines = new List<string> { provider.Name + " · " + provider.Description };
        if (Surface.PageCount > 1) lines.Add($"Page {Surface.Page + 1}/{Surface.PageCount} · scroll to see more providers");
        if (state.Error != null) lines.Add(state.Error);
        if (state.Reading is { } r)
        {
            lines.Add($"Last successful refresh: {r.Fetched.LocalDateTime:ddd d MMM, HH:mm:ss}");
            foreach (var q in r.Quotas)
            {
                lines.Add($"{q.Label}: {(q.Used is { } n ? $"{n:0}% used · {100-n:0}% left" : q.Note == "Unlimited" ? "unlimited" : "unavailable")}");
                if (q.Reset != null) lines.Add($"Resets {q.Reset.Value.LocalDateTime:ddd d MMM, HH:mm:ss}");
                if (q.Note != null) lines.Add(q.Note);
            }
            lines.Add(r.Balance);
            if (provider.HasFreeResets) lines.Add(r.FreeResets is { } count ? $"Free resets available: {count}" : "Free reset availability: unavailable");
        }
        return string.Join("\n", lines);
    }
    private async Task RefreshUsage()
    {
        if (closing) return;
        Surface.Updating = true; Surface.InvalidateVisual();
        await Monitor.RefreshAsync();
        if (closing) return;
        nextRefresh = DateTimeOffset.Now.AddMinutes(1);
        Surface.Updating = false; UpdateDisplay();
    }
    private void UpdateDisplay()
    {
        if (closing) return;
        if (!changingLayout && (dragOffset == null || !Monitor.Preferences.Layout.IsIsland())
            && (Monitor.Preferences.Layout.IsIsland() || Monitor.Preferences.Layout.IsAdditional())
            && (Width != Surface.DesiredWidgetSize.Width || Height != Surface.DesiredWidgetSize.Height))
        {
            if (Surface.HoveredIslandProviderId != null)
            {
                ApplyLayout(false);
                Position = ConstrainPosition(new((int)Math.Round(islandHoverCenter - Width * RenderScaling / 2), islandHoverOrigin!.Value.Y));
            }
            else ApplyLayout(Monitor.Preferences.Layout is WidgetLayout.AdaptiveIsland or WidgetLayout.SpotlightIsland);
        }
        Surface.InvalidateVisual();
        var summary = string.Join(" | ", Monitor.Enabled.Select(p => p.Name + " " + string.Join(" / ", Monitor.States[p.Id].Reading?.Quotas.Select(q => q.Used is { } n ? $"{(Monitor.Preferences.ShowUsed ? n : 100 - n):0}% {(Monitor.Preferences.ShowUsed ? "used" : "left")}" : "unavailable") ?? ["unavailable"])));
        if (tray != null) tray.ToolTipText = summary.Length > 127 ? summary[..124] + "…" : summary;
        AutomationProperties.SetHelpText(Surface, string.Join("\n", Monitor.Enabled.Select(Details)));
        WriteHealth();
    }
    internal void Restore() { ClampPosition(); Show(); Activate(); taskbarOverlay?.EnsureAboveTaskbar(); WriteHealth(); }
    private void ToggleVisible() { if (IsVisible) { SetAdaptiveHover(null); Hide(); WriteHealth(); } else Restore(); }
    // Windows islands may overlap the taskbar. macOS keeps floating windows out
    // of the menu bar and Dock; other layouts retain their desktop-only bounds.
    internal static PixelRect PositionArea(PixelRect bounds, PixelRect workingArea, bool island, bool windows) =>
        island && windows ? bounds : workingArea;
    private void OnScreensChanged(object? sender, EventArgs e) { if (initialized) { SetAdaptiveHover(null); ApplyLayout(false); } }
    private PixelPoint ConstrainPosition(PixelPoint requested)
    {
        var screen = Screens.ScreenFromPoint(requested) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        PixelRect? bar = null;
        if (Monitor.Preferences.Layout.IsIsland())
        {
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.HandleDescriptor == "HWND" && screen != null)
                bar = WindowsTaskbarOverlay.TaskbarOn(screen.Bounds);
            var height = bar is { } taskbar ? ScreenPlacement.TaskbarHeight(taskbar.Height, screen!.Scaling) : 40;
            if (Surface.IslandHeight != height || Height != height)
            {
                Surface.IslandHeight = height;
                Height = Surface.Height = height;
                Surface.InvalidateVisual();
            }
            if (bar is { } target) requested = ScreenPlacement.AlignTaskbar(requested, height, screen!.Scaling, target);
        }
        return ScreenPlacement.Constrain(requested, new Size(Width, Height), Screens.All.Select(display =>
            (PositionArea(display.Bounds, display.WorkingArea, Monitor.Preferences.Layout.IsIsland(), OperatingSystem.IsWindows()), display.Scaling)));
    }
    private void ClampPosition()
    {
        if (clamping || !double.IsFinite(Width) || !double.IsFinite(Height)) return;
        clamping = true;
        try { Position = ConstrainPosition(Position); }
        finally { clamping = false; }
    }
    private void ResetPosition()
    {
        SetAdaptiveHover(null);
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea is { } a)
            Position = Monitor.Preferences.Layout.IsIsland()
                ? new(a.X + (a.Width - (int)(Width * RenderScaling)) / 2, a.Y + (int)(8 * RenderScaling))
                : new(a.Right - (int)(Width * RenderScaling) - 18, a.Bottom - (int)(Height * RenderScaling) - 18);
        ClampPosition();
        SavePosition();
    }
    private void SavePosition()
    {
        var position = islandHoverOrigin ?? Position;
        Monitor.Preferences.X = position.X; Monitor.Preferences.Y = position.Y; SavePreferences();
    }
    private void SavePreferences() { if (persist) Monitor.Preferences.Save(); }
    private void WriteHealth()
    {
        if (!persist || closing) return;
        try
        {
            Directory.CreateDirectory(Preferences.Folder);
            var data = new { Version = Program.AppVersion, Platform = OperatingSystem.IsMacOS() ? "macOS" : "Windows", Updated = DateTimeOffset.Now, ProcessId = Environment.ProcessId,
                Theme = Monitor.Preferences.DarkMode ? "dark" : "light", Layout = Monitor.Preferences.Layout switch { WidgetLayout.DetailedCards => "cards", WidgetLayout.RoundBadges => "round", _ => Monitor.Preferences.Layout.Id() }, WindowVisible = IsVisible, AlwaysOnTop = Topmost, WindowBounds = new { Left = Position.X, Top = Position.Y, Width, Height },
                IslandStyle = Monitor.Preferences.IslandStyle.ToString(), Updates = updates?.Status,
                Providers = Monitor.Enabled.ToDictionary(p => p.Id, p => new { Monitor.States[p.Id].Reading, Monitor.States[p.Id].Error }) };
            var path = Path.Combine(Preferences.Folder, "status.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    internal static Reading Fixture(ProviderDefinition p) => new(p.Name, p.QuotaLabels.Select((label, i) => new Quota(label, i == 0 ? 25 : 60, DateTimeOffset.Now.AddHours(i == 0 ? 3 : 48))).ToArray(), p.Id == "grok-bot" ? "On-demand  US$0.00 / $40.00" : "Extra credits  US$20.00", DateTimeOffset.Now, p.HasFreeResets ? 3 : null);
    internal void SaveRender(string name)
    {
        Surface.Measure(new(Width, Height)); Surface.Arrange(new(0, 0, Width, Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(Width * 2), (int)(Height * 2)), new Vector(192, 192));
        bitmap.Render(Surface); bitmap.Save(Path.Combine(AppContext.BaseDirectory, name));
    }
    private async Task RenderChecks()
    {
        try
        {
            foreach (var only in Monitor.Providers)
            {
                foreach (var p in Monitor.Providers) Monitor.SetEnabled(p, true);
                foreach (var p in Monitor.Providers.Where(p => p != only)) Monitor.SetEnabled(p, false);
                Monitor.States[only.Id].Reading = Fixture(only);
                foreach (var compact in new[] { false, true })
                {
                    SetCompact(compact);
                    foreach (var used in new[] { false, true })
                    {
                        SetPercentage(used);
                        await Task.Delay(150);
                        if (compact && Width != 96 || !compact && Height != 71 + only.CardHeight) throw new InvalidOperationException("Single-provider layout failed.");
                        SaveRender($"{only.Id}-{(compact ? "badge" : "cards")}-{(used ? "used" : "left")}.png");
                    }
                }
            }
            foreach (var p in Monitor.Providers) { Monitor.SetEnabled(p, true); Monitor.States[p.Id].Reading = Fixture(p); }
            foreach (var dark in new[] { false, true })
            {
                SetDarkMode(dark);
                foreach (var layout in WidgetLayouts.MenuOrder)
                {
                    SetLayout(layout); Surface.ShowProvider(Monitor.Enabled[0].Id); SetPercentage(false);
                    await Task.Delay(100);
                    SaveRender($"render-{layout.Id()}{(dark ? "-dark" : "")}.png");
                    if (layout.IsIsland())
                    {
                        foreach (var style in Enum.GetValues<IslandStyle>())
                        {
                            SetIslandStyle(style);
                            var suffix = (style == IslandStyle.ProviderPills ? "-pills" : "") + (dark ? "-dark" : "");
                            SaveRender($"render-{layout.Id()}{suffix}.png");
                            if (layout == WidgetLayout.AdaptiveIsland)
                            {
                                SetAdaptiveHover(Surface.VisibleProviders[0].Id);
                                SaveRender($"render-adaptive-island-expanded{suffix}.png");
                                SetAdaptiveHover(null);
                            }
                        }
                        SetIslandStyle(IslandStyle.Continuous);
                    }
                }
            }
            SetDarkMode(false);
            SetPercentage(false); SetCompact(false); await Task.Delay(150); SaveRender("cards-after-switch.png");
            SetCompact(true); Hide(); Restore(); await Task.Delay(150); SaveRender("widget-preview.png");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "render-checks.txt"), "PASS: all eleven layouts in light/dark, both island styles, adaptive expansion, native single-provider used/remaining rendering, layout switching and hide/restore.\n");
            Quit();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "render-error.txt"), ex.ToString()); PrepareExit(); (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(1); }
    }
    internal void PrepareExit()
    {
        if (closing) return;
        SavePosition(); closing = true; islandCollapseTimer.Stop(); updates?.Dispose(); taskbarOverlay?.Dispose(); timer.Stop(); Monitor.Dispose(); grok.Dispose(); tray?.Dispose();
    }
    internal void Quit() { PrepareExit(); Close(); (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(); }
    internal static string SafeError(Exception ex) => ex switch
    {
        OperationCanceledException => "Refresh timed out. Retrying automatically.", HttpRequestException => "Cannot connect. Check your network.",
        JsonException => "Usage response changed or login file is invalid.", IOException => "Cannot read the CLI login. Try again after sign-in.",
        InvalidOperationException => ex.Message, _ => "Usage unavailable. Check the provider login."
    };
    internal static string ResetText(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset == null) return "Reset time unavailable";
        var d = reset.Value - now;
        if (d <= TimeSpan.Zero) return "Reset due · awaiting update";
        return d.TotalDays >= 1 ? $"Resets in {(int)d.TotalDays}d {d.Hours}h" : d.TotalHours >= 1 ? $"Resets in {(int)d.TotalHours}h {d.Minutes}m" : $"Resets in {Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))}m";
    }
}
