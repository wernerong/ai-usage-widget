using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageWidget;

internal static class ClaudeProvider
{
    internal static async Task<Reading> Read(CancellationToken ct)
    {
        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(ProviderApi.Home, ".claude");
        JsonElement credentials = default;
        if (OperatingSystem.IsMacOS())
        {
            var suffix = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { } custom
                ? "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(custom))).ToLowerInvariant()[..8] : "";
            var secret = await Task.Run(() => CredentialVault.ReadMac("Claude Code-credentials" + suffix), ct);
            if (secret != null) { using var doc = JsonDocument.Parse(secret); credentials = doc.RootElement.Clone(); }
        }
        if (credentials.ValueKind != JsonValueKind.Object) credentials = await ProviderApi.ReadJson(Path.Combine(folder, ".credentials.json"), ct);
        var oauth = Json.Get(credentials, "claudeAiOauth");
        var token = ProviderApi.Require(Json.Str(oauth, "accessToken"), "Sign in to Claude Code with a Claude subscription using /login. API keys do not expose this allowance.");
        return Parse(await ProviderApi.Request(ProviderApi.Client, "Claude", "https://api.anthropic.com/api/oauth/usage", token, ct, beta: "oauth-2025-04-20"));
    }
    internal static Reading Parse(JsonElement root)
    {
        Quota Window(string key, string label) { var value = Json.Get(root, key); return new(label, ProviderApi.Percent(Json.Num(value, "utilization")), Json.Date(Json.Get(value, "resets_at"))); }
        var quotas = new[] { Window("five_hour", "5 hours"), Window("seven_day", "Weekly") };
        var modelDetails = new[] { Window("seven_day_sonnet", "Sonnet weekly"), Window("seven_day_opus", "Opus weekly") }.Where(q => q.Used != null);
        quotas[1] = quotas[1] with { Note = string.Join("\n", modelDetails.Select(q => $"{q.Label}: {q.Used:0}% used")) };
        var extra = Json.Get(root, "extra_usage");
        var balance = Json.Get(extra, "is_enabled").ValueKind == JsonValueKind.False ? "Extra usage disabled" : "Extra usage unavailable";
        if (Json.Get(extra, "is_enabled").ValueKind == JsonValueKind.True && Json.Num(extra, "used_credits") is { } used)
            balance = $"Extra usage  US${used / 100:0.00}" + (Json.Num(extra, "monthly_limit") is { } limit ? $" / ${limit / 100:0.00}" : "");
        return ProviderApi.Result("Claude", quotas, balance);
    }
}

internal static class CursorProvider
{
    internal static async Task<Reading> Read(CancellationToken ct)
    {
        string? token = null;
        if (OperatingSystem.IsMacOS()) token = await Task.Run(() => CredentialVault.ReadMac("cursor-access-token", "cursor-user"), ct);
        var path = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "auth.json") : Path.Combine(ProviderApi.Home, ".cursor", "auth.json");
        var auth = await ProviderApi.ReadJson(path, ct);
        token ??= Json.Str(auth, "accessToken");
        token = ProviderApi.Require(token, "Sign in to Cursor CLI with agent login on this computer. The widget reads the CLI subscription login.");
        var config = await ProviderApi.ReadJson(Path.Combine(Environment.GetEnvironmentVariable("CURSOR_CONFIG_DIR") ?? Path.Combine(ProviderApi.Home, ".cursor"), "cli-config.json"), ct);
        var authInfo = Json.Get(config, "authInfo");
        var team = Json.Num(authInfo, "activeTeamId", "teamId")?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Parse(await ProviderApi.Request(ProviderApi.Client, "Cursor", "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage", token, ct, new { }, team: team));
    }
    internal static Reading Parse(JsonElement root)
    {
        var plan = Json.Get(root, "planUsage");
        // includedSpend is a proto3 scalar: an omitted field is zero within an explicitly returned plan.
        var included = Json.Get(plan, "includedSpend").ValueKind == JsonValueKind.Undefined && plan.ValueKind == JsonValueKind.Object ? 0 : Json.Num(plan, "includedSpend");
        var percent = ProviderApi.Percent(Json.Num(plan, "totalPercentUsed")) ?? ProviderApi.Ratio(included, Json.Num(plan, "limit"));
        var note = "Current billing cycle included allowance.";
        foreach (var (field, label) in new[] { ("autoPercentUsed", "Auto"), ("apiPercentUsed", "API") })
            if (ProviderApi.Percent(Json.Num(plan, field)) is { } used) note += $"\n{label}: {used:0}% used";
        var spend = Json.Get(root, "spendLimitUsage");
        var spending = Json.Get(spend, "individualUsed").ValueKind == JsonValueKind.Undefined && spend.ValueKind == JsonValueKind.Object ? 0 : Json.Num(spend, "individualUsed");
        var balance = spending is { } cents ? $"On-demand  US${cents / 100:0.00}" : "On-demand unavailable";
        return ProviderApi.Result("Cursor", [new("Monthly", percent, Json.Date(Json.Get(root, "billingCycleEnd")), note)], balance);
    }
}

internal static class CopilotProvider
{
    internal static async Task<Reading> Read(CancellationToken ct)
    {
        var token = await ProviderApi.Saved("copilot");
        if (token == null)
        {
            var paths = new[] { Path.Combine(ProviderApi.Home, ".config", "github-copilot"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "github-copilot") };
            foreach (var folder in paths)
            {
                foreach (var file in new[] { "apps.json", "hosts.json" })
                {
                    var doc = await ProviderApi.ReadJson(Path.Combine(folder, file), ct);
                    if (doc.ValueKind != JsonValueKind.Object) continue;
                    var accounts = doc.EnumerateObject().Where(p => p.Name == "github.com" || p.Name.StartsWith("github.com:", StringComparison.Ordinal)).Select(p => Json.Str(p.Value, "oauth_token")).Where(t => t != null).Distinct().ToArray();
                    if (accounts.Length > 1) throw new InvalidOperationException("Multiple Copilot logins found. Choose one by adding its token in Connections → Copilot.");
                    if (accounts.Length == 1) { token = accounts[0]; break; }
                }
                if (token != null) break;
            }
        }
        if (token == null)
        {
            var binary = OperatingSystem.IsMacOS() && File.Exists("/opt/homebrew/bin/gh") ? "/opt/homebrew/bin/gh" : OperatingSystem.IsMacOS() && File.Exists("/usr/local/bin/gh") ? "/usr/local/bin/gh" : "gh";
            try { token = await ProviderApi.Run(binary, ["auth", "token", "--hostname", "github.com"], ct); }
            catch (OperationCanceledException) { throw; }
            catch { throw new InvalidOperationException("Sign in with GitHub CLI (gh auth login), or add a Copilot-authorized GitHub token in Connections → Copilot."); }
        }
        return Parse(await ProviderApi.Request(ProviderApi.Client, "Copilot", "https://api.github.com/copilot_internal/user", ProviderApi.Require(token, "Connect your Copilot account."), ct));
    }
    internal static Reading Parse(JsonElement root)
    {
        var reset = Json.Date(Json.Get(root, "quota_reset_date_utc", "quota_reset_date", "limited_user_reset_date"));
        var snapshot = Json.Get(root, "quota_snapshots");
        Quota Window(string key, string label)
        {
            var item = Json.Get(snapshot, key);
            var unlimited = Json.Get(item, "unlimited").ValueKind == JsonValueKind.True;
            var percent = unlimited ? null : ProviderApi.UsedFromRemaining(Json.Num(item, "percent_remaining"));
            if (!unlimited && percent == null)
            {
                var total = Json.Num(Json.Get(root, "monthly_quotas"), key);
                var remaining = Json.Num(Json.Get(root, "limited_user_quotas"), key);
                percent = ProviderApi.Ratio(total - remaining, total);
            }
            return new(label, percent, Json.Date(Json.Get(item, "quota_reset_at")) ?? reset, unlimited ? "Unlimited" : null);
        }
        return ProviderApi.Result("Copilot", [Window("premium_interactions", "Premium"), Window("chat", "Chat")], "GitHub Copilot allowance");
    }
}
