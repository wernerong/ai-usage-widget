using UsageWidget;

internal static class AutomaticUpdateChecks
{
    internal static async Task Run()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        Check(new Preferences().AutomaticUpdates, "Automatic updates default on for existing preferences");
        var config = System.Text.Json.Nodes.JsonNode.Parse("""{"statusLine":{"command":"\"old.exe\" --capture-antigravity","enabled":false},"theme":"dark"}""")!.AsObject();
        Check(AntigravityProvider.MigrateCommand(config, "old.exe", "current.exe") &&
            config["statusLine"]!["enabled"]!.GetValue<bool>() == false && config["theme"]!.GetValue<string>() == "dark",
            "Installer migration preserves status-line enablement and unrelated settings");
        Check(!AntigravityProvider.MigrateCommand(config, "other.exe", "new.exe"), "Migration leaves custom hook commands untouched");
        var fake = new Fake(); var applied = 0; var enabled = false;
        using var updates = new AutomaticUpdates(fake, () => enabled, () => true, () => applied++);
        await updates.Check();
        Check(fake.Calls == 0 && applied == 0, "Opt-out skips download and restart");
        await updates.Check(manual: true);
        Check(fake.Calls == 1 && applied == 0 && updates.Status == "Up to date", "Manual check works while opted out without an available update");
        enabled = true; fake.Ready = true;
        await updates.Check();
        Check(applied == 1, "Verified update applies automatically");
        fake.Fail = true;
        await updates.Check();
        Check(applied == 1 && updates.Status.Contains("retry"), "Download failure leaves widget running and retryable");
        fake.Fail = false; fake.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = updates.Check();
        var calls = fake.Calls;
        await updates.Check();
        Check(fake.Calls == calls, "Concurrent checks cannot download or restart twice");
        enabled = false; fake.Block.SetResult(); await first;
        Check(applied == 1, "Opting out during download prevents automatic restart");
        fake.Block = null; fake.Available = false;
        await updates.Check(manual: true);
        Check(fake.Calls == calls, "Unpackaged copies do not attempt updates");
        fake.Available = true;
        using var deferred = new AutomaticUpdates(fake, () => true, () => false, () => applied++);
        var wait = deferred.Check();
        deferred.Dispose(); await wait;
        Check(applied == 1, "Open dialog defers restart and quitting cancels the wait");
        Console.WriteLine($"PASS: {count} automatic update lifecycle checks.");
    }
    private sealed class Fake : IWidgetUpdates
    {
        public bool Available { get; set; } = true;
        public bool Ready, Fail;
        public int Calls;
        public TaskCompletionSource? Block;
        public async Task<bool> Download(CancellationToken cancellation)
        {
            Calls++;
            if (Block != null) await Block.Task.WaitAsync(cancellation);
            if (Fail) throw new IOException("Synthetic corrupt download");
            return Ready;
        }
        public void Apply(bool hidden) => throw new NotSupportedException();
    }
}
