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
        var choice = await ProviderApi.ReadJson(CopilotVSCode.ConnectionPath, ct);
        if (Json.Str(choice, "Mode") == "direct" && Json.Str(choice, "Auth") == "gcm") return await CopilotGitHub.Read(choice, ct);
        if (await CopilotVSCode.Read(ct) is { } snapshot) return snapshot;
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
        var premium = Json.Get(snapshot, "premium_interactions");
        var tokenBilling = Json.Get(root, "token_based_billing").ValueKind == JsonValueKind.True || Json.Get(premium, "token_based_billing").ValueKind == JsonValueKind.True;
        if (!tokenBilling) return ProviderApi.Result("Copilot", [Window("premium_interactions", "Premium"), Window("chat", "Chat")], "GitHub Copilot allowance");
        if (Json.Str(root, "copilot_plan") == "free" && (premium.ValueKind != JsonValueKind.Object || Json.Num(premium, "entitlement") == 0))
            premium = Json.Get(snapshot, "chat"); // Free plans can allocate credits to chat only.
        var plan = Json.Str(root, "copilot_plan") switch { "business" => "Copilot Business", "enterprise" => "Copilot Enterprise", "free" => "Copilot Free", _ => "Copilot" };
        double? credits = Json.Num(premium, "credits_used") is >= 0 and var used ? used : null;
        var entitlement = Json.Num(premium, "entitlement");
        var unlimited = Json.Get(premium, "unlimited").ValueKind == JsonValueKind.True;
        var noPool = unlimited && Json.Get(premium, "has_quota").ValueKind == JsonValueKind.False;
        var note = $"{plan} · monthly AI credits. Uses GitHub's reported allowance; company pool and budget policies can also constrain access.";
        if (credits is { } consumed) note += $"\nCredits used: {consumed:0.##}";
        if (!unlimited && entitlement is > 0) note += $"\nReported allowance: {entitlement:0.##} credits";
        if (Json.Get(premium, "overage_permitted").ValueKind == JsonValueKind.False) note += "\nAdditional usage is disabled by policy.";
        string? status = noPool ? "Empty" : unlimited ? "No cap" : entitlement == 0 ? "Blocked" : null;
        double? percent = null;
        if (!unlimited && entitlement != 0)
        {
            percent = ProviderApi.UsedFromRemaining(Json.Num(premium, "percent_remaining"));
            // quota_remaining and entitlement share a denominator. credits_used
            // does not: VS Code explicitly avoids dividing it by entitlement.
            if (percent == null && Json.Num(premium, "quota_remaining") is { } remaining && entitlement is > 0)
                percent = ProviderApi.Ratio(Math.Max(0, entitlement.Value - remaining), entitlement);
        }
        if (noPool) note += "\nThe organization's shared credit limit has been reached; no pool percentage was reported.";
        else if (unlimited) note += "\nNo personal cap was reported. This is not an unlimited company credit pool.";
        else if (entitlement == 0) note += "\nGitHub reported zero allocated credits; no percentage can be calculated.";
        var main = new Quota("Credits", percent, Json.Date(Json.Get(premium, "quota_reset_at")) ?? reset, note, status);
        var extraLimit = Json.Num(premium, "overage_entitlement");
        var extraUsed = Json.Num(premium, "overage_count");
        var extra = new Quota("Additional", ProviderApi.Ratio(extraUsed, extraLimit), main.Reset,
            extraLimit is > 0 ? $"Additional usage: {extraUsed:0.##} / {extraLimit:0.##}. GitHub's additional allowance is separate from the main credits." : "No additional usage budget was reported.");
        if (main.Used == null && main.Status == null && extra.Used == null)
            throw new InvalidOperationException("Copilot did not return a supported credit allowance. Check your plan and connection; no percentage is estimated.");
        return new("Copilot", [main, extra], credits is { } amount ? $"Credits used  {amount:0.##}" : plan + " allowance", DateTimeOffset.Now);
    }
}
