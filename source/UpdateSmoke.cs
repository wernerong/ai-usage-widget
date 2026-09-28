using Velopack;
using Velopack.Sources;

namespace UsageWidget;

// Exercised in disposable installed copies by CI. This diagnostic accepts only a
// local directory, never an alternate network feed or production credentials.
internal static class UpdateSmoke
{
    internal static int WriteLaunchAgent(string directory)
    {
        if (!OperatingSystem.IsMacOS() || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return 4;
        var agent = PlatformServices.LaunchAgent(Environment.ProcessPath!);
        var dict = agent.Root!.Element("dict")!;
        ((System.Xml.Linq.XElement)dict.Elements("key").Single(k => k.Value == "Label").NextNode!).Value =
            "com.wernerong.ai-usage-widget.update-check-" + Guid.NewGuid().ToString("N");
        dict.Element("array")!.Add(new System.Xml.Linq.XElement("string", "--update-smoke"), new System.Xml.Linq.XElement("string", directory));
        agent.Save(Path.Combine(directory, "update-check.plist"));
        return 0;
    }

    internal static async Task<int> Run(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return 4;
        // macOS normally shares its cache across every copy of an app ID.
        // Keep local smoke packages out of the user's real updater cache and
        // make corruption tests repeatable even when rebuilding one version.
        Velopack.Locators.IVelopackLocator? locator = null;
        if (OperatingSystem.IsMacOS())
        {
            var cache = Path.Combine(directory, "cache");
            Directory.CreateDirectory(cache);
            locator = new SmokeMacLocator(cache);
        }
        var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(directory)), locator: locator);
        if (!manager.IsInstalled) return 3;
        var update = await manager.CheckForUpdatesAsync();
        if (update == null)
        {
            File.WriteAllText(Path.Combine(directory, "updated-version.txt"), Program.AppVersion);
            return 0;
        }
        await manager.DownloadUpdatesAsync(update);
        manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: true,
            restartArgs: ["--update-smoke", directory]);
        return 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private sealed class SmokeMacLocator(string cache) : Velopack.Locators.OsxVelopackLocator(null, null)
    {
        public override string PackagesDir => cache;
    }
}
