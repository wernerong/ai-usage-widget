using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace UsageWidget;

internal static class PlatformServices
{
    internal const string LaunchAgentId = "com.wernerong.ai-usage-widget";
    public static string SettingsFolder => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AIUsageWidget")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageWidget");
    public static string LaunchAgentPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", LaunchAgentId + ".plist");
    public static string StartupLabel => OperatingSystem.IsMacOS() ? "Start at login" : "Start with Windows";
    public static bool StartupEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("AIUsageWidget") != null;
        }
        return OperatingSystem.IsMacOS() && File.Exists(LaunchAgentPath);
    }
    internal static XDocument LaunchAgent(string executable) => new(new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
        new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
            new XElement("key", "Label"), new XElement("string", LaunchAgentId),
            new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", executable)),
            new XElement("key", "RunAtLoad"), new XElement("true"),
            new XElement("key", "ProcessType"), new XElement("string", "Interactive"))));
    public static void SetStartup(bool enabled)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate this application.");
        if (enabled && Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Install a published build before enabling startup.");
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) key.SetValue("AIUsageWidget", $"\"{executable}\""); else key.DeleteValue("AIUsageWidget", false);
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (enabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
                LaunchAgent(executable).Save(LaunchAgentPath + ".tmp");
                File.Move(LaunchAgentPath + ".tmp", LaunchAgentPath, true);
            }
            else File.Delete(LaunchAgentPath);
        }
        else throw new PlatformNotSupportedException("Startup is supported on Windows and macOS.");
    }
    public static void OpenUrl(string url)
    {
        if (OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            info.ArgumentList.Add(url);
            Process.Start(info)?.Dispose();
        }
        else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    }
}

// Per-user file ownership and a pipe replace Windows-only named events.
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream ownership;
    private readonly CancellationTokenSource stop = new();
    private readonly string pipeName;
    private SingleInstance(FileStream ownership, string pipeName) { this.ownership = ownership; this.pipeName = pipeName; }
    internal static string PipeName(string folder) => "AIUsageWidget-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..24];
    public static SingleInstance? Acquire(string? folder = null)
    {
        folder ??= Preferences.Folder;
        Directory.CreateDirectory(folder);
        var pipe = PipeName(folder);
        try { return new(new FileStream(Path.Combine(folder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), pipe); }
        catch (IOException)
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            client.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
            client.WriteByte(1);
            return null;
        }
    }
    public async Task Listen(Action show)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                var message = new byte[1];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                if (await server.ReadAsync(message, timeout.Token) == 1 && message[0] == 1) show();
            }
            catch (OperationCanceledException) { }
            catch (IOException) when (!stop.IsCancellationRequested) { await Task.Delay(200); }
        }
    }
    public void Dispose() { stop.Cancel(); ownership.Dispose(); }
}
