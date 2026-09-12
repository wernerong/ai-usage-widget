using Avalonia.Media;

namespace UsageWidget;

internal sealed class UsageMonitor : IDisposable
{
    public Preferences Preferences { get; }
    public ProviderDefinition[] Providers { get; }
    public Dictionary<string, ProviderState> States { get; } = new();
    public ProviderDefinition[] Enabled => Providers.Where(Preferences.IsEnabled).ToArray();
    private readonly Dictionary<string, CancellationTokenSource> requests = new();
    private readonly CancellationTokenSource stop = new();
    private Task? refresh;
    private bool requested, disposed;
    public bool Busy => refresh is { IsCompleted: false };
    public event Action? Changed;
    public UsageMonitor(Preferences preferences, ProviderDefinition[] providers)
    {
        Preferences = preferences; Providers = providers;
        if (providers.Length == 0) throw new ArgumentException("At least one provider is required.");
        if (!Enabled.Any()) Preferences.SetEnabled(providers[0], true);
        foreach (var p in providers) States[p.Id] = new(p.Name);
    }
    public bool SetEnabled(ProviderDefinition provider, bool enabled)
    {
        if (disposed || Preferences.IsEnabled(provider) == enabled || (!enabled && Enabled.Length <= 1)) return false;
        Preferences.SetEnabled(provider, enabled);
        if (!enabled)
        {
            if (requests.TryGetValue(provider.Id, out var request)) request.Cancel();
            // An old response cannot overwrite the new state, even if its reader ignores cancellation.
            States[provider.Id] = new(provider.Name);
        }
        requested = true;
        Changed?.Invoke();
        return true;
    }
    public Task RefreshAsync()
    {
        if (disposed) return Task.CompletedTask;
        if (Busy) { requested = true; return refresh!; }
        refresh = RefreshLoop();
        return refresh;
    }
    private async Task RefreshLoop()
    {
        do
        {
            requested = false;
            await Task.WhenAll(Enabled.Select(Update));
            if (!disposed) Changed?.Invoke();
        } while (requested && !disposed);
    }
    private async Task Update(ProviderDefinition provider)
    {
        var state = States[provider.Id];
        using var request = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        requests[provider.Id] = request;
        try
        {
            var reading = await provider.Read(request.Token);
            if (!request.IsCancellationRequested) { state.Reading = reading; state.Error = null; }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { state.Error = Widget.SafeError(ex); }
        finally { requests.Remove(provider.Id); }
        state.Attempted = DateTimeOffset.Now;
        if (!disposed) Changed?.Invoke();
    }
    public void Dispose() { disposed = true; stop.Cancel(); }
}

internal static class MonitorChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        var prefs = new Preferences();
        int readsA = 0, readsB = 0;
        var waiting = new TaskCompletionSource<Reading>();
        CancellationToken activeToken = default;
        var sample = new Reading("Test", [new("Weekly", 20, null)], "Credits", DateTimeOffset.Now);
        var a = new ProviderDefinition("a", "A", "", "", Colors.Black, ["Weekly"], false, true, _ => { readsA++; return Task.FromResult(sample); });
        var b = a with { Id = "b", Name = "B", Read = ct => { readsB++; activeToken = ct; return waiting.Task; } };
        using var monitor = new UsageMonitor(prefs, [a, b]);
        monitor.SetEnabled(b, false);
        await monitor.RefreshAsync();
        Check(readsA == 1 && readsB == 0, "Disabled provider never starts a request");
        Check(!monitor.SetEnabled(a, false) && monitor.Enabled.Length == 1, "Cannot disable the last provider");
        monitor.SetEnabled(b, true);
        var pending = monitor.RefreshAsync();
        monitor.SetEnabled(b, false);
        Check(activeToken.IsCancellationRequested, "Disabling cancels an in-flight request");
        waiting.SetResult(sample);
        await pending;
        Check(monitor.States["b"].Reading == null && monitor.States["b"].Error == null, "Late result cannot resurrect a disabled provider");
        monitor.SetEnabled(b, true);
        await monitor.RefreshAsync();
        Check(monitor.States["b"].Reading == sample && readsB == 2, "Re-enabling fetches a fresh result");
        var failing = a with { Read = _ => Task.FromException<Reading>(new HttpRequestException()) };
        using var errors = new UsageMonitor(new(), [failing]);
        errors.States["a"].Reading = sample;
        await errors.RefreshAsync();
        Check(errors.States["a"].Stale && errors.States["a"].Reading == sample, "Refresh failure preserves last good reading");
        var executable = "/Users/Example & Co/Applications/AI Usage Widget.app/Contents/MacOS/AIUsageWidget";
        var xml = PlatformServices.LaunchAgent(executable).ToString();
        Check(System.Xml.Linq.XDocument.Parse(xml).Descendants("array").Single().Element("string")!.Value == executable, "macOS startup escapes spaces and XML characters without shell quoting");
        Check(SingleInstance.PipeName("/user/one") != SingleInstance.PipeName("/user/two"), "Instance activation is isolated by settings location");
        var testFolder = Path.Combine(Path.GetTempPath(), "AIUsageWidget-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testFolder);
        try
        {
            using var owner = SingleInstance.Acquire(testFolder)!;
            var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = owner.Listen(() => activated.TrySetResult());
            var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(typeof(Program).Assembly.Location);
            info.ArgumentList.Add("--instance-probe"); info.ArgumentList.Add(testFolder);
            using var child = System.Diagnostics.Process.Start(info)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await child.WaitForExitAsync(deadline.Token);
                await activated.Task.WaitAsync(deadline.Token);
                Check(child.ExitCode == 0, "Second process restores owner through the per-user pipe");
            }
            finally
            {
                if (!child.HasExited) child.Kill(true);
                owner.Dispose();
                await listener;
            }
        }
        finally { File.Delete(Path.Combine(testFolder, "instance.lock")); Directory.Delete(testFolder); }
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "checks.txt"), $"PASS: {count} refresh/platform checks\n");
    }
}
