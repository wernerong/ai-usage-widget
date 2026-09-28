using Velopack;
using Velopack.Sources;

namespace UsageWidget;

// Exercised in disposable installed copies by CI. This diagnostic accepts only a
// local directory, never an alternate network feed or production credentials.
internal static class UpdateSmoke
{
    internal static async Task<int> Run(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return 4;
        var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(directory)));
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
}
