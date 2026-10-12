using System.Text.Json;
using UsageWidget;

internal static class CopilotBusinessChecks
{
    internal static void Run()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        JsonElement Data(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
        void Reject(Action read, string name) { try { read(); throw new Exception("FAIL: " + name); } catch (InvalidOperationException) { count++; } }
        const string capped = """{"token_based_billing":true,"copilot_plan":"business","quota_reset_date_utc":"2026-11-01T00:00:00Z","quota_snapshots":{"premium_interactions":{"percent_remaining":64.6,"unlimited":false,"has_quota":false,"entitlement":"1000","credits_used":97,"quota_remaining":999,"overage_entitlement":200,"overage_count":20}}}""";
        var reading = CopilotProvider.Parse(Data(capped));
        Check(Math.Abs(reading.Quotas[0].Used!.Value - 35.4) < 0.001, "GitHub percentage overrides conflicting spend and remaining counts");
        Check(reading.Quotas[0].Status == null && reading.Quotas[0].Note!.Contains("Business"), "Capped Business ignores has_quota=false sentinel and identifies plan");
        Check(reading.Quotas[0].Label == "Credits" && reading.Quotas[0].Reset == DateTimeOffset.Parse("2026-11-01T00:00:00Z"), "Credits retain monthly reset");
        Check(reading.Quotas[1].Used == 10 && reading.Quotas[1].Label == "Additional", "Additional quota remains separate");
        Check(CopilotProvider.Parse(Data("""{"token_based_billing":true,"quota_snapshots":{"premium_interactions":{"entitlement":1000,"quota_remaining":750,"credits_used":10}}}""")).Quotas[0].Used == 25, "Remaining and entitlement use the same verified basis");
        Reject(() => CopilotProvider.Parse(Data("""{"token_based_billing":true,"quota_snapshots":{"premium_interactions":{"entitlement":1000,"credits_used":250}}}""")), "credits used alone never supplies a percentage denominator");
        var uncapped = CopilotProvider.Parse(Data("""{"token_based_billing":true,"copilot_plan":"business","quota_snapshots":{"premium_interactions":{"unlimited":true,"has_quota":true,"percent_remaining":100,"credits_used":240}}}"""));
        Check(uncapped.Quotas[0].Used == null && uncapped.Quotas[0].Status == "No cap" && uncapped.Balance.Contains("240"), "Uncapped Business reports usage without an invented 100 percent remaining");
        var empty = CopilotProvider.Parse(Data("""{"token_based_billing":true,"quota_snapshots":{"premium_interactions":{"unlimited":true,"has_quota":false,"credits_used":240}}}"""));
        Check(empty.Quotas[0].Used == null && empty.Quotas[0].Status == "Empty", "Depleted organization pool does not appear unlimited or invent a percentage");
        var blocked = CopilotProvider.Parse(Data("""{"token_based_billing":true,"quota_snapshots":{"premium_interactions":{"unlimited":false,"entitlement":0,"percent_remaining":100}}}"""));
        Check(blocked.Quotas[0].Used == null && blocked.Quotas[0].Status == "Blocked", "Zero budget is blocked without a 0/0 ratio");
        var free = CopilotProvider.Parse(Data("""{"token_based_billing":true,"copilot_plan":"free","quota_snapshots":{"premium_interactions":{"entitlement":0,"percent_remaining":0},"chat":{"entitlement":100,"percent_remaining":60}}}"""));
        Check(free.Quotas[0].Used == 40 && free.Quotas[0].Status == null, "Free chat credits are not blocked by an unallocated premium category");
        var id = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow;
        var envelope = JsonSerializer.Serialize(new { Schema = 1, ConnectionId = id, Account = "work-example", Host = "github.com", Fetched = now.AddMinutes(-8), Usage = Data(capped) });
        var bridged = CopilotVSCode.ParseSnapshot(Data(envelope), id, now);
        Check(bridged.Fetched == now.AddMinutes(-8) && bridged.Quotas[0].Note!.Contains("work-example"), "Closed VS Code keeps original freshness and account identity");
        Check(new ProviderState("Copilot") { Reading = bridged }.Stale, "Old snapshot is stale rather than newly fetched");
        Reject(() => CopilotVSCode.ParseSnapshot(Data(envelope), Guid.NewGuid().ToString(), now), "another account/profile connection cannot be reused");
        Reject(() => CopilotVSCode.ParseSnapshot(Data(envelope.Replace("github.com", "untrusted.example")), id, now), "unsupported snapshot host");
        Reject(() => CopilotVSCode.ParseSnapshot(Data(JsonSerializer.Serialize(new { Schema = 1, ConnectionId = id, Host = "github.com", Fetched = now.AddMinutes(10), Usage = Data(capped) })), id, now), "future snapshot freshness");
        Reject(() => CopilotVSCode.ParseSnapshot(Data(JsonSerializer.Serialize(new { Schema = 1, ConnectionId = id, Error = "signed_out" })), id, now), "auth error clears live state instead of substituting a CLI account");
        Check(CopilotVSCode.ValidHost("example.ghe.com") && !CopilotVSCode.ValidHost("example.ghe.com.evil.test"), "GHE host boundary");
        var legacy = CopilotVSCode.ParseSnapshot(Data(JsonSerializer.Serialize(new { Schema = 1, ConnectionId = id, Host = "github.com", Fetched = now, Usage = Data("""{"quota_snapshots":{"premium_interactions":{"unlimited":true}}}""") })), id, now);
        Check(legacy.Quotas[0].Status == "Unlimited", "Bridge metadata does not hide legacy unlimited allowance");
        Console.WriteLine($"PASS: {count} Copilot Business and VS Code snapshot checks.");
    }
}
