using Velopack;
using Velopack.Sources;

namespace UsageWidget;

internal interface IWidgetUpdates
{
    bool Available { get; }
    Task<bool> Download(CancellationToken cancellation);
    void Apply(bool hidden);
}

internal sealed class GithubWidgetUpdates : IWidgetUpdates
{
    internal const string Repository = "https://github.com/wernerong/ai-usage-widget";
    private readonly UpdateManager manager = new(new GithubSource(Repository, null, false));
    private VelopackAsset? ready;
    public bool Available => manager.IsInstalled;
    public async Task<bool> Download(CancellationToken cancellation)
    {
        // The installed package selects win-x64, osx-arm64 or osx-x64. Never opt
        // into pre-releases or downgrades, and never include a GitHub token.
        var update = await manager.CheckForUpdatesAsync();
        cancellation.ThrowIfCancellationRequested();
        if (update == null) return false;
        await manager.DownloadUpdatesAsync(update, cancelToken: cancellation);
        ready = update.TargetFullRelease;
        return true;
    }
    public void Apply(bool hidden)
    {
        if (ready == null) throw new InvalidOperationException("No verified update is ready.");
        // Start the helper before shutting down. If starting it fails, the
        // widget stays alive; the helper waits for our normal graceful exit.
        manager.WaitExitThenApplyUpdates(ready, silent: true, restart: true,
            restartArgs: hidden ? ["--hidden"] : []);
    }
}

internal sealed class AutomaticUpdates : IDisposable
{
    private readonly IWidgetUpdates updater;
    private readonly Func<bool> enabled;
    private readonly Func<bool> canRestart;
    private readonly Action apply;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    public string Status { get; private set; } = "Automatic updates enabled";
    public event Action? Changed;
    public bool Available => updater.Available;
    internal AutomaticUpdates(IWidgetUpdates updater, Func<bool> enabled, Func<bool> canRestart, Action apply)
    {
        this.updater = updater; this.enabled = enabled; this.canRestart = canRestart; this.apply = apply;
        if (!Available) Status = "Install the latest release to enable updates";
    }
    private void SetStatus(string status) { Status = status; Changed?.Invoke(); }
    public async Task Run()
    {
        if (!Available) return;
        try
        {
            // Spread requests from machines that start at the same time.
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(30, 90)), stop.Token);
            while (!stop.IsCancellationRequested)
            {
                if (enabled()) await Check();
                await Task.Delay(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(Random.Shared.Next(0, 30)), stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public async Task Check(bool manual = false)
    {
        if (!Available || (!manual && !enabled()) || !await gate.WaitAsync(0)) return;
        try
        {
            SetStatus("Checking for updates…");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            if (!await updater.Download(timeout.Token)) { SetStatus("Up to date"); return; }
            SetStatus("Update ready — waiting for widget to be free");
            while (!canRestart()) await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            // Disabling while a download is in flight also prevents the restart.
            if (!manual && !enabled()) { SetStatus("Update ready; automatic updates paused"); return; }
            apply();
        }
        catch (OperationCanceledException) { SetStatus("Update paused; will retry later"); }
        catch { SetStatus("Could not update; will retry later"); }
        finally { gate.Release(); }
    }
    public void Dispose() => stop.Cancel();
}
