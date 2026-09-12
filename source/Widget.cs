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

namespace UsageWidget;

internal sealed class Widget : Window
{
    internal readonly UsageMonitor Monitor;
    internal readonly ProviderSurface Surface;
    private readonly GrokProvider grok = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TrayIcon? tray;
    private readonly bool persist;
    private readonly string[] args;
    private bool closing, initialized;
    private readonly PixelPoint? initialPosition;
    private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
    private string lastTip = "";
    internal static ProviderDefinition[] CreateProviders(GrokProvider grok) => [
        new("codex", "Codex", "https://chatgpt.com/codex/settings/usage", "live Codex account limits", Color.Parse("#3675C4"), ["5 hours", "Weekly"], true, true, CodexProvider.Read),
        new("grok", "Grok", "https://grok.com?_s=usage", "Grok CLI billing", Color.Parse("#8074AA"), ["Weekly"], false, true, grok.Read)
    ];
    public Widget(string[] args, UsageMonitor? monitor = null, bool createTray = true, bool persist = true)
    {
        this.args = args;
        this.persist = persist && !args.Contains("--render") && !args.Contains("--render-check");
        var preferences = args.Contains("--render-check") ? new Preferences() : Preferences.Load();
        Monitor = monitor ?? new UsageMonitor(preferences, CreateProviders(grok));
        if (args.Contains("--compact")) Monitor.Preferences.Compact = true;
        if (args.Contains("--cards")) Monitor.Preferences.Compact = false;
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
        AutomationProperties.SetName(Surface, "AI Usage Widget");
        using (var stream = AssetLoader.Open(new Uri("avares://AIUsageWidget/Assets/widget.png"))) Icon = new WindowIcon(stream);
        if (createTray)
        {
            tray = new TrayIcon { Icon = Icon, ToolTipText = "AI Usage Widget", IsVisible = true };
            tray.Clicked += (_, _) => ToggleVisible();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
        }
        Monitor.Changed += UpdateDisplay;
        ApplyLayout(false);
        BuildMenus();
        PositionChanged += (_, _) => SavePosition();
        Closing += (_, e) => { if (!closing) { e.Cancel = true; Hide(); WriteHealth(); } };
        Closed += (_, _) => Surface.Dispose();
        Opened += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            if (initialPosition is { } savedPosition) Position = savedPosition; else ResetPosition();
            ClampPosition();
            SavePosition();
            if (args.Contains("--render-check")) { await RenderChecks(); return; }
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
        Surface.PointerMoved += (_, e) => UpdateTooltip(e.GetPosition(Surface));
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { Hide(); WriteHealth(); e.Handled = true; }
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
            new("Layout", Children: [new("Round badges (compact)", () => SetCompact(true), prefs.Compact, Radio: true), new("Detailed cards", () => SetCompact(false), !prefs.Compact, Radio: true)]),
            new("Always on top", () => { prefs.Pinned = Topmost = !prefs.Pinned; SavePreferences(); BuildMenus(); }, prefs.Pinned),
            new("Percentage display", Children: [new("Percentage remaining", () => SetPercentage(false), !prefs.ShowUsed, Radio: true), new("Percentage used", () => SetPercentage(true), prefs.ShowUsed, Radio: true)]),
            new("Opacity", Children: new[] {100,97,85,70}.Select(n => new MenuChoice($"{n}%", () => { prefs.Opacity = n / 100.0; ApplyOpacity(); SavePreferences(); BuildMenus(); }, Math.Abs(prefs.Opacity - n / 100.0) < 0.001, Radio: true)).ToArray()),
            new(PlatformServices.StartupLabel, () => {
                try { PlatformServices.SetStartup(!PlatformServices.StartupEnabled()); Surface.Notice = null; }
                catch (Exception ex) { Surface.Notice = "Could not change startup: " + ex.Message; _ = ShowError(Surface.Notice); }
                BuildMenus(); UpdateDisplay();
            }, PlatformServices.StartupEnabled()),
            new("-")
        };
        choices.AddRange(Monitor.Enabled.Select(p => new MenuChoice($"Open {p.Name} usage", () => {
            try { PlatformServices.OpenUrl(p.UsageUrl); } catch (Exception ex) { _ = ShowError("Could not open the browser: " + ex.Message); }
        })));
        choices.AddRange([new("Reset position", () => { ResetPosition(); Restore(); }), new("-"), new($"AI Usage Widget v{Program.AppVersion}", Enabled: false), new("Exit", Quit)]);
        return choices.ToArray();
    }
    internal void BuildMenus()
    {
        var choices = Choices();
        var context = new ContextMenu();
        foreach (var choice in choices) context.Items.Add(ContextItem(choice));
        Surface.ContextMenu = context;
        if (tray != null)
        {
            var native = new NativeMenu();
            foreach (var choice in choices) native.Items.Add(NativeItem(choice));
            tray.Menu = native;
        }
        static Control ContextItem(MenuChoice choice)
        {
            if (choice.Label == "-") return new Separator();
            var item = new MenuItem { Header = choice.Label, IsEnabled = choice.Enabled, IsChecked = choice.Checked ?? false,
                ToggleType = choice.Checked == null ? MenuItemToggleType.None : choice.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox };
            if (choice.Children != null) foreach (var child in choice.Children) item.Items.Add(ContextItem(child));
            if (choice.Action != null) item.Click += (_, e) => { e.Handled = true; choice.Action(); };
            return item;
        }
        static NativeMenuItemBase NativeItem(MenuChoice choice)
        {
            if (choice.Label == "-") return new NativeMenuItemSeparator();
            var item = new NativeMenuItem(choice.Label) { IsEnabled = choice.Enabled, IsChecked = choice.Checked ?? false,
                ToggleType = choice.Checked == null ? NativeMenuItemToggleType.None : choice.Radio ? NativeMenuItemToggleType.Radio : NativeMenuItemToggleType.CheckBox };
            if (choice.Children != null) { item.Menu = new(); foreach (var child in choice.Children) item.Menu.Items.Add(NativeItem(child)); }
            if (choice.Action != null) item.Click += (_, _) => choice.Action();
            return item;
        }
    }
    private async Task ShowError(string message)
    {
        var dialog = new Window { Title = "AI Usage Widget", Width = 380, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close } };
        Restore();
        await dialog.ShowDialog(this);
    }
    internal void SetProvider(ProviderDefinition provider, bool enabled)
    {
        if (!Monitor.SetEnabled(provider, enabled)) return;
        ApplyLayout(true); SavePreferences(); BuildMenus();
        if (!args.Contains("--render-check")) _ = RefreshUsage();
    }
    internal void SetPercentage(bool used) { Monitor.Preferences.ShowUsed = used; SavePreferences(); BuildMenus(); UpdateDisplay(); }
    internal void SetCompact(bool compact) { Monitor.Preferences.Compact = compact; ApplyLayout(true); SavePreferences(); BuildMenus(); UpdateDisplay(); }
    private void ApplyOpacity() => Surface.Opacity = Math.Clamp(Monitor.Preferences.Opacity, 0.5, 1);
    private void ApplyLayout(bool keepBottomRight)
    {
        var oldWidth = Width; var oldHeight = Height;
        var size = Surface.DesiredWidgetSize;
        Width = Surface.Width = size.Width; Height = Surface.Height = size.Height;
        ApplyOpacity();
        if (keepBottomRight && double.IsFinite(oldWidth) && double.IsFinite(oldHeight))
            Position = new(Position.X + (int)((oldWidth - Width) * RenderScaling), Position.Y + (int)((oldHeight - Height) * RenderScaling));
        ClampPosition();
        lastTip = ""; ToolTip.SetTip(Surface, null);
    }
    private async void OnSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(Surface);
        if (Monitor.Preferences.Compact)
        {
            if (Surface.ProviderAt(p) == null) return;
            if (e.ClickCount == 2) SetCompact(false); else BeginMoveDrag(e);
        }
        else if (p.Y < 44)
        {
            if (p.X >= 282) Surface.ContextMenu?.Open(Surface);
            else if (p.X >= 248) { Hide(); WriteHealth(); }
            else if (p.X >= 215) await RefreshUsage();
            else BeginMoveDrag(e);
        }
    }
    private void UpdateTooltip(Point point)
    {
        var p = Surface.ProviderAt(point);
        var text = p != null ? Details(p) : Surface.Notice ?? "Drag to move · right-click for settings";
        if (!Monitor.Preferences.Compact && point.Y < 44)
            text = point.X >= 282 ? "Settings and Exit" : point.X >= 248 ? "Hide to tray / menu bar" : point.X >= 215 ? "Refresh now" : "Drag to move";
        if (Monitor.Preferences.Compact) text += "\n\nDrag to move · double-click for cards · right-click for settings";
        if (lastTip != text) { lastTip = text; ToolTip.SetTip(Surface, text); }
    }
    internal string Details(ProviderDefinition provider)
    {
        var state = Monitor.States[provider.Id];
        var lines = new List<string> { provider.Name + " · " + provider.Description };
        if (state.Error != null) lines.Add(state.Error);
        if (state.Reading is { } r)
        {
            lines.Add($"Last successful refresh: {r.Fetched.LocalDateTime:ddd d MMM, HH:mm:ss}");
            foreach (var q in r.Quotas)
            {
                lines.Add($"{q.Label}: {(q.Used is { } n ? $"{n:0.##}% used · {100-n:0.##}% left" : "unavailable")}");
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
        Surface.InvalidateVisual();
        var summary = string.Join(" | ", Monitor.Enabled.Select(p => p.Name + " " + string.Join(" / ", Monitor.States[p.Id].Reading?.Quotas.Select(q => q.Used is { } n ? $"{(Monitor.Preferences.ShowUsed ? n : 100 - n):0.#}% {(Monitor.Preferences.ShowUsed ? "used" : "left")}" : "unavailable") ?? ["unavailable"])));
        if (tray != null) tray.ToolTipText = summary.Length > 127 ? summary[..124] + "…" : summary;
        AutomationProperties.SetHelpText(Surface, string.Join("\n", Monitor.Enabled.Select(Details)));
        WriteHealth();
    }
    internal void Restore() { ClampPosition(); Show(); Activate(); WriteHealth(); }
    private void ToggleVisible() { if (IsVisible) { Hide(); WriteHealth(); } else Restore(); }
    private void ClampPosition()
    {
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea;
        if (area is not { } a || !double.IsFinite(Width)) return;
        Position = new(Math.Clamp(Position.X, a.X, Math.Max(a.X, a.Right - (int)(Width * RenderScaling))), Math.Clamp(Position.Y, a.Y, Math.Max(a.Y, a.Bottom - (int)(Height * RenderScaling))));
    }
    private void ResetPosition()
    {
        if (Screens.Primary?.WorkingArea is { } a) Position = new(a.Right - (int)(Width * RenderScaling) - 18, a.Bottom - (int)(Height * RenderScaling) - 18);
        SavePosition();
    }
    private void SavePosition() { Monitor.Preferences.X = Position.X; Monitor.Preferences.Y = Position.Y; SavePreferences(); }
    private void SavePreferences() { if (persist) Monitor.Preferences.Save(); }
    private void WriteHealth()
    {
        if (!persist || closing) return;
        try
        {
            Directory.CreateDirectory(Preferences.Folder);
            var data = new { Version = Program.AppVersion, Platform = OperatingSystem.IsMacOS() ? "macOS" : "Windows", Updated = DateTimeOffset.Now, ProcessId = Environment.ProcessId,
                Layout = Monitor.Preferences.Compact ? "round" : "cards", WindowVisible = IsVisible, AlwaysOnTop = Topmost, WindowBounds = new { Left = Position.X, Top = Position.Y, Width, Height },
                Providers = Monitor.Enabled.ToDictionary(p => p.Id, p => new { Monitor.States[p.Id].Reading, Monitor.States[p.Id].Error }) };
            var path = Path.Combine(Preferences.Folder, "status.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    internal static Reading Fixture(ProviderDefinition p) => new(p.Name, p.QuotaLabels.Select((label, i) => new Quota(label, i == 0 ? 25 : 60, DateTimeOffset.Now.AddHours(i == 0 ? 3 : 48))).ToArray(), "Extra credits  US$20.00", DateTimeOffset.Now, p.HasFreeResets ? 3 : null);
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
            SetPercentage(false); SetCompact(false); await Task.Delay(150); SaveRender("cards-after-switch.png");
            SetCompact(true); Hide(); Restore(); await Task.Delay(150); SaveRender("widget-preview.png");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "render-checks.txt"), "PASS: native single/both-provider layout, used/remaining rendering, cards/badge switching and hide/restore.\n");
            Quit();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "render-error.txt"), ex.ToString()); PrepareExit(); (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(1); }
    }
    internal void PrepareExit()
    {
        if (closing) return;
        SavePosition(); closing = true; timer.Stop(); Monitor.Dispose(); grok.Dispose(); tray?.Dispose();
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
