using System.Text.Json;
using Avalonia.Media;

namespace UsageWidget;

internal sealed class Preferences
{
    // Missing entries remain enabled for existing providers; future providers opt in.
    public Dictionary<string, bool> Providers { get; set; } = new();
    public bool IsEnabled(ProviderDefinition provider) => Providers?.GetValueOrDefault(provider.Id, provider.EnabledByDefault) ?? provider.EnabledByDefault;
    public void SetEnabled(ProviderDefinition provider, bool enabled)
    {
        Providers ??= new();
        Providers[provider.Id] = enabled;
    }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Pinned { get; set; } = true;
    public bool ShowUsed { get; set; }
    public bool Compact { get; set; }
    public bool Island { get; set; }
    public bool DarkMode { get; set; }
    public double Opacity { get; set; } = 0.97;
    public static string Folder => PlatformServices.SettingsFolder;
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        catch { /* A read-only settings directory must not stop live monitoring. */ }
    }
}

internal sealed record ProviderDefinition(string Id, string Name, string UsageUrl, string Description,
    Color Accent, string[] QuotaLabels, bool HasFreeResets, bool EnabledByDefault,
    Func<CancellationToken, Task<Reading>> Read)
{
    public int CardHeight => 54 + 53 * QuotaLabels.Length;
}

internal sealed class ProviderState(string name)
{
    public string Name { get; } = name;
    public Reading? Reading { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? Attempted { get; set; }
    public bool Stale => Error != null || Reading is { } r && DateTimeOffset.Now - r.Fetched > TimeSpan.FromMinutes(3);
}
