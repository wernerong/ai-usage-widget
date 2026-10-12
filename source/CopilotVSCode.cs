using System.Diagnostics;
using System.Text.Json;

namespace UsageWidget;

internal static class CopilotVSCode
{
    internal const string ExtensionId = "wernerong.ai-usage-widget-copilot";
    internal const string PackageName = "AIUsageWidget-Copilot-VSCode.vsix";
    internal static string ConnectionPath => Path.Combine(Preferences.Folder, "copilot-connection.json");
    internal static string SnapshotPath => Path.Combine(Preferences.Folder, "copilot-vscode-usage.json");
    internal static bool ValidHost(string? host) => host == "github.com" || host != null &&
        System.Text.RegularExpressions.Regex.IsMatch(host, @"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.ghe\.com$");
    internal static void SelectSource(string mode, string? id = null)
    {
        Directory.CreateDirectory(Preferences.Folder);
        File.WriteAllText(ConnectionPath + ".tmp", JsonSerializer.Serialize(new { Schema = 1, Mode = mode, Id = id ?? Guid.NewGuid().ToString() }));
        File.Move(ConnectionPath + ".tmp", ConnectionPath, true);
    }
    internal static string PairingId(JsonElement choice) => Json.Num(choice, "Schema") == 1 &&
        Json.Str(choice, "Mode") == "vscode" && Guid.TryParse(Json.Str(choice, "Id"), out var id)
        ? id.ToString() : Guid.NewGuid().ToString();
    internal static async Task<Reading?> Read(CancellationToken ct)
    {
        var choice = await ProviderApi.ReadJson(ConnectionPath, ct);
        if (choice.ValueKind == JsonValueKind.Undefined || Json.Str(choice, "Mode") == "direct") return null;
        if (Json.Num(choice, "Schema") != 1 || Json.Str(choice, "Mode") != "vscode" || !Guid.TryParse(Json.Str(choice, "Id"), out _))
            throw new InvalidOperationException("Reconnect Copilot through Connections → Copilot.");
        if (!File.Exists(SnapshotPath)) throw new InvalidOperationException("Finish connecting your work GitHub account in VS Code. Choose AI Usage Widget: Connect Copilot Account if no prompt appeared.");
        if (new FileInfo(SnapshotPath).Length > 65536) throw new InvalidOperationException("The VS Code usage snapshot is invalid. Reconnect Copilot.");
        using var stream = new FileStream(SnapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseSnapshot(document.RootElement, Json.Str(choice, "Id")!, DateTimeOffset.UtcNow);
    }
    internal static Reading ParseSnapshot(JsonElement snapshot, string connectionId, DateTimeOffset now)
    {
        if (Json.Num(snapshot, "Schema") != 1 || Json.Str(snapshot, "ConnectionId") != connectionId)
            throw new InvalidOperationException("Connect your selected Copilot account in VS Code. Another connection's usage is not displayed.");
        if (Json.Str(snapshot, "Error") is { } error)
            throw new InvalidOperationException(error switch {
                "signed_out" or "unauthorized" => "Copilot was signed out. Connect your work GitHub account again in VS Code.",
                "forbidden" => "GitHub or your company rejected Copilot usage access. Check the account and your organization's access policy.",
                "rate_limited" => "GitHub asked the VS Code connection to slow down. It will retry automatically.",
                "disconnected" => "The VS Code connection is disconnected. Reconnect it from Connections → Copilot.",
                _ => "VS Code could not refresh Copilot usage. Open VS Code and choose AI Usage Widget: Refresh Copilot Usage." });
        var fetched = Json.Date(Json.Get(snapshot, "Fetched"));
        if (!ValidHost(Json.Str(snapshot, "Host")) || fetched == null || fetched > now.AddMinutes(1))
            throw new InvalidOperationException("The VS Code usage snapshot is invalid. Reconnect Copilot.");
        var reading = CopilotProvider.Parse(Json.Get(snapshot, "Usage"));
        var account = Json.Str(snapshot, "Account") ?? "Selected GitHub account";
        account = new string(account.Where(c => !char.IsControl(c)).Take(100).ToArray());
        var source = $"VS Code · {account} · {Json.Str(snapshot, "Host")}";
        return reading with { Fetched = fetched.Value, Quotas = reading.Quotas.Select(q => q with {
            Status = q.Status ?? (q.Note == "Unlimited" ? "Unlimited" : null),
            Note = string.Join("\n", new[] { q.Note, source, "Live while VS Code is open; otherwise the last snapshot is marked stale." }.Where(s => !string.IsNullOrWhiteSpace(s))) }).ToArray() };
    }
    internal static (string Binary, string Cli)? FindCode()
    {
        var roots = OperatingSystem.IsWindows() ? new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code") }
            : new[] { "/Applications/Visual Studio Code.app/Contents", Path.Combine(ProviderApi.Home, "Applications", "Visual Studio Code.app", "Contents") };
        foreach (var root in roots)
        {
            var binary = Path.Combine(root, OperatingSystem.IsWindows() ? "Code.exe" : "MacOS/Electron");
            if (!File.Exists(binary)) continue;
            var cli = Path.Combine(root, OperatingSystem.IsWindows() ? "resources/app/out/cli.js" : "Resources/app/out/cli.js");
            if (File.Exists(cli)) return (binary, cli);
            // Recent Windows versions keep resources in a commit-specific folder.
            var wrapper = Path.Combine(root, "bin", "code.cmd");
            if (File.Exists(wrapper))
            {
                var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(wrapper), @"\.\.\\([0-9a-f]{10})\\resources\\app\\out\\cli\.js");
                if (match.Success && File.Exists(cli = Path.Combine(root, match.Groups[1].Value, "resources/app/out/cli.js"))) return (binary, cli);
            }
        }
        return null;
    }
    internal static async Task InstallPackage(CancellationToken ct)
    {
        var package = Path.Combine(AppContext.BaseDirectory, PackageName);
        if (!File.Exists(package)) throw new InvalidOperationException("Install the latest widget release to get the bundled VS Code connection.");
        var code = FindCode() ?? throw new InvalidOperationException("Desktop VS Code was not found in its standard location. Install the bundled VSIX from VS Code's Extensions menu, then run AI Usage Widget: Connect Copilot Account.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(1));
        using var process = new Process { StartInfo = new(code.Binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        process.StartInfo.Environment.Remove("VSCODE_DEV");
        foreach (var argument in new[] { code.Cli, "--install-extension", package, "--force" }) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token); await output; await error;
            if (process.ExitCode != 0) throw new InvalidOperationException("VS Code could not install the connection. Check whether your company permits local extensions, or use the existing CLI connection.");
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
        Directory.CreateDirectory(Preferences.Folder);
        File.WriteAllText(Path.Combine(Preferences.Folder, "copilot-extension-version.txt"), Program.AppVersion);
    }
    internal static async Task UpdateInstalledConnection()
    {
        // Once opted in, new widget packages can update the bundled connection
        // without opening account selection or changing the user's consent.
        try
        {
            var selected = await ProviderApi.ReadJson(ConnectionPath, CancellationToken.None);
            if (Json.Str(selected, "Mode") != "vscode") return;
            var marker = Path.Combine(Preferences.Folder, "copilot-extension-version.txt");
            if (File.Exists(marker) && File.ReadAllText(marker) == Program.AppVersion) return;
            await InstallPackage(CancellationToken.None);
        }
        catch { /* The existing bridge keeps working if VS Code cannot be updated. */ }
    }
    internal static async Task InstallAndConnect(CancellationToken ct)
    {
        await InstallPackage(ct);
        var id = PairingId(await ProviderApi.ReadJson(ConnectionPath, ct));
        SelectSource("vscode", id);
        PlatformServices.OpenUrl($"vscode://{ExtensionId}/connect?id={id}");
    }
}
