using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsageWidget;

internal static class AntigravityProvider
{
    internal static string SnapshotPath => Path.Combine(Preferences.Folder, "antigravity-usage.json");
    internal static string SettingsPath => Path.Combine(ProviderApi.Home, ".gemini", "antigravity-cli", "settings.json");
    internal static Reading Parse(JsonElement root)
    {
        var quota = Json.Get(root, "quota");
        var rows = quota.ValueKind == JsonValueKind.Object ? quota.EnumerateObject().Select(p => new Quota(
            p.Name, ProviderApi.UsedFromRemaining(Json.Num(p.Value, "remaining_fraction"), 1), Json.Date(Json.Get(p.Value, "reset_time")))).ToArray() : [];
        return ProviderApi.Result("Antigravity", [ProviderApi.Tightest("Models", rows)], "Antigravity CLI quota snapshot");
    }
    internal static async Task<Reading> Read(CancellationToken ct)
    {
        if (!File.Exists(SnapshotPath)) throw new InvalidOperationException("Choose Connections → Antigravity → Connect status line, then open Antigravity CLI and run /usage. Quotas update when its status line runs.");
        var value = await ProviderApi.ReadJson(SnapshotPath, ct);
        var reading = value.Deserialize<Reading>();
        if (reading == null || reading.Fetched > DateTimeOffset.Now.AddMinutes(1)) throw new InvalidOperationException("Antigravity quota snapshot is invalid. Run /usage in Antigravity CLI.");
        return reading;
    }
    internal static int Capture()
    {
        try
        {
            using var json = JsonDocument.Parse(Console.In.ReadToEnd());
            var reading = Parse(json.RootElement);
            Directory.CreateDirectory(Preferences.Folder);
            var temporary = SnapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(reading));
            File.Move(temporary, SnapshotPath, true);
            Console.WriteLine($"Antigravity · {100-reading.Quotas[0].Used:0}% left");
            return 0;
        }
        catch { Console.WriteLine("Antigravity quota unavailable"); return 0; } // Never log the raw status input: it contains account/workspace information.
    }
    internal static JsonObject ConnectSettings(string existing, string executable)
    {
        var settings = JsonNode.Parse(existing) as JsonObject ?? throw new InvalidOperationException("Antigravity settings must be a JSON object.");
        var current = settings["statusLine"]?["command"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(current) && !current.Contains("--capture-antigravity", StringComparison.Ordinal))
            throw new InvalidOperationException("An Antigravity status line already exists. Keep it and follow the README integration instructions to add the widget capture command.");
        // Installed executable path is trusted; reject shell metacharacters before writing a shell command.
        if (executable.IndexOfAny(['"', '\n', '\r', '`', '$', '%', '&', '|', ';']) >= 0)
            throw new InvalidOperationException("Use the standard widget installation path to connect the status line.");
        settings["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = $"\"{executable}\" --capture-antigravity", ["enabled"] = true };
        return settings;
    }
    internal static void Connect()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Widget executable not found.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Connect from the installed widget.");
        var text = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";
        var settings = ConnectSettings(text, executable);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        if (File.Exists(SettingsPath)) File.Copy(SettingsPath, SettingsPath + ".widget-backup-" + DateTimeOffset.Now.ToUnixTimeMilliseconds());
        File.WriteAllText(SettingsPath + ".widget-tmp", settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(SettingsPath + ".widget-tmp", SettingsPath, true);
    }
}
