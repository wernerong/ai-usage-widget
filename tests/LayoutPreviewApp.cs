using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using UsageWidget;

// A real native window with synthetic readers, no tray, no settings writes and
// no updater. It can run beside the installed widget for manual visual checks.
internal sealed class LayoutPreviewApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var grok = new GrokProvider();
            var providers = Widget.CreateProviders(grok).Select(p => p with { Read = _ => Task.FromResult(Widget.Fixture(p)) }).ToArray();
            var preferences = new Preferences { Layout = WidgetLayouts.FromArguments(desktop.Args ?? [], WidgetLayout.CompactList) };
            foreach (var provider in providers) preferences.SetEnabled(provider, true);
            var monitor = new UsageMonitor(preferences, providers);
            var widget = new Widget(["--render-check"], monitor, createTray: false, persist: false)
            {
                Title = "AI Usage Widget — layout preview (sample data)", ShowActivated = false
            };
            widget.SetLayout(preferences.Layout);
            desktop.MainWindow = widget;
            desktop.ShutdownRequested += (_, _) => widget.PrepareExit();
            desktop.Exit += (_, _) => { widget.PrepareExit(); grok.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
