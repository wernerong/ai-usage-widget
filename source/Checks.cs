using System.Text.Json;
using Avalonia.Media;

namespace UsageWidget;

internal static class Checks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        Reading Codex(string json) { using var d = JsonDocument.Parse(json); return CodexProvider.Parse(d.RootElement); }
        Reading Grok(string json) { using var d = JsonDocument.Parse(json); return GrokProvider.Parse(d.RootElement); }
        var codex = Codex("""{"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":21,"windowDurationMins":10080,"resetsAt":1900000000},"secondary":{"usedPercent":42,"windowDurationMins":300},"credits":{"balance":"0"}}}}""");
        Check(codex.Quotas[0].Used == 42 && codex.Quotas[1].Used == 21, "Prefer account bucket; map by actual window length");
        Check(codex.Quotas[1].Reset == DateTimeOffset.FromUnixTimeSeconds(1900000000), "Unix reset mapping");
        var missing = Codex("""{"rateLimits":{"primary":{"windowDurationMins":300}}}""");
        Check(missing.Quotas.All(q => q.Used == null), "Missing Codex readings stay unknown");
        Check(missing.FreeResets == null, "Missing reset count stays unavailable");
        var resets = Codex("""{"rateLimits":{},"rateLimitResetCredits":{"availableCount":3,"credits":[]}}""");
        Check(resets.FreeResets == 3, "Reset count is authoritative even when details are empty");
        var noResets = Codex("""{"rateLimits":{},"rateLimitResetCredits":{"availableCount":0,"credits":null}}""");
        Check(noResets.FreeResets == 0, "Zero available resets stays zero");
        var g = Grok("""{"config":{"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","end":"2026-09-10T08:54:45+08:00"},"creditUsagePercent":1.0,"prepaidBalance":{"val":2000}}}""");
        Check(g.Quotas[0].Used == 1, "Grok small percentage is not a fraction");
        Check(g.Balance.EndsWith("US$20.00"), "Grok cents convert to dollars");
        var omitted = Grok("""{"config":{"isUnifiedBillingUser":true,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","end":"2026-09-10T08:54:45+08:00"},"prepaidBalance":{}}}""");
        Check(omitted.Quotas[0].Used == 0 && omitted.Quotas[0].Note != null, "Match official Grok CLI omitted percentage behavior");
        Check(omitted.Balance.EndsWith("US$0.00"), "Proto3 empty Cent equals zero");
        var monthly = Grok("""{"config":{"monthlyLimit":{"val":2000},"used":{"val":0}}}""");
        Check(monthly.Quotas[0].Used == null && monthly.Quotas[0].Label != "Weekly", "Never relabel monthly billing as weekly quota");
        var now = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        Check(Widget.ResetText(now.AddMinutes(-1), now).Contains("awaiting"), "Reset does not invent fresh allowance");
        Check(Widget.ResetText(now.AddDays(2).AddHours(3), now) == "Resets in 2d 3h", "Countdown formatting");
        Check(CodexProvider.Clamp(101) == 100 && CodexProvider.Clamp(-1) == 0 && CodexProvider.Clamp(null) == null, "Clamp valid values only");
        var state = new ProviderState("Test") { Reading = g, Error = "Network" };
        Check(state.Stale && state.Reading == g, "Failure preserves last result and marks stale");
        var definition = new ProviderDefinition("codex", "Codex", "", "", Colors.Black, ["5 hours", "Weekly"], true, true,
            _ => Task.FromResult(codex));
        var preferences = JsonSerializer.Deserialize<Preferences>("{\"Compact\":true}")!;
        Check(preferences.IsEnabled(definition), "Legacy preferences retain enabled subscriptions");
        preferences.SetEnabled(definition, false);
        var restored = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;
        Check(!restored.IsEnabled(definition), "Disabled subscription survives settings round trip");
        Check(!restored.IsEnabled(definition with { Id = "future", EnabledByDefault = false }), "Future integrations can default to opt-in");
        restored.SetEnabled(definition, true);
        Check(restored.IsEnabled(definition), "Subscription can be re-enabled");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "checks.txt"), $"PASS: {count} checks\n");
    }
}
