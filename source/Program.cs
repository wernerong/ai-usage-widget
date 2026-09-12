using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Text.Json;

namespace UsageWidget;

internal static class Program
{
    internal static string AppVersion => typeof(Program).Assembly.GetName().Version!.ToString(3);
    internal static SingleInstance? Instance;
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--enable-startup") { PlatformServices.SetStartup(true); return 0; }
            if (args.Length == 1 && args[0] == "--disable-startup") { PlatformServices.SetStartup(false); return 0; }
            if (args.Length == 2 && args[0] == "--instance-probe")
            {
                using var probe = SingleInstance.Acquire(args[1]);
                return probe == null ? 0 : 2;
            }
            if (args.Contains("--version")) { Console.WriteLine(AppVersion); return 0; }
            if (args.Contains("--self-test")) { Checks.Run(); MonitorChecks.Run().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--diagnose"))
            {
                using var grok = new GrokProvider();
                var preferences = Preferences.Load();
                var results = Task.WhenAll(Widget.CreateProviders(grok).Where(preferences.IsEnabled)
                    .Select(async provider => { try { return (object)await provider.Read(CancellationToken.None); } catch (Exception ex) { return new { provider.Name, Error = Widget.SafeError(ex) }; } })).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            using var instance = SingleInstance.Acquire();
            if (instance == null) return 0;
            Instance = instance;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Directory.CreateDirectory(Preferences.Folder);
            File.WriteAllText(Path.Combine(Preferences.Folder, "startup-error.txt"), ex.ToString());
            return 1;
        }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var widget = new Widget(desktop.Args ?? []);
            desktop.MainWindow = widget;
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
                activation.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) widget.Restore(); };
            desktop.ShutdownRequested += (_, _) => widget.PrepareExit();
            if (Program.Instance is { } instance) _ = instance.Listen(() => Dispatcher.UIThread.Post(widget.Restore));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
