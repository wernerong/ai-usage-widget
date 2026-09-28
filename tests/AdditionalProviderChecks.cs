using System.Net;
using System.Text.Json;
using UsageWidget;

internal static class AdditionalProviderChecks
{
    public static async Task Run()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        JsonElement JsonOf(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
        void Reject(Func<Reading> parse, string name)
        {
            try { parse(); throw new Exception("FAIL: accepted " + name); }
            catch (InvalidOperationException) { count++; }
        }
        var claude = ClaudeProvider.Parse(JsonOf("""{"five_hour":{"utilization":25.5,"resets_at":"2030-01-02T00:00:00Z"},"seven_day":{"utilization":80},"extra_usage":{"is_enabled":false}}"""));
        Check(claude.Quotas[0].Used == 25.5 && claude.Quotas[1].Used == 80, "Claude percentages are USED, retaining precision until display");
        Check(claude.Quotas[0].Reset == DateTimeOffset.Parse("2030-01-02T00:00:00Z") && claude.Balance.Contains("disabled"), "Claude reset and disabled extra usage");
        Reject(() => ClaudeProvider.Parse(JsonOf("""{"five_hour":{"utilization":-1}}""")), "negative Claude usage");
        Reject(() => ClaudeProvider.Parse(JsonOf("{}")), "missing Claude quotas");
        var cursor = CursorProvider.Parse(JsonOf("""{"planUsage":{"totalPercentUsed":40,"autoPercentUsed":30,"apiPercentUsed":70},"billingCycleEnd":"1893542400000","spendLimitUsage":{"individualUsed":123}}"""));
        Check(cursor.Quotas[0].Used == 40 && cursor.Quotas[0].Note!.Contains("API: 70%"), "Cursor total is authoritative; pools are not added");
        Check(cursor.Quotas[0].Reset != null && cursor.Balance.Contains("1.23"), "Cursor milliseconds and cents");
        Check(CursorProvider.Parse(JsonOf("""{"planUsage":{"includedSpend":500,"limit":2000}}""")).Quotas[0].Used == 25, "Cursor explicit used/limit fallback");
        Check(CursorProvider.Parse(JsonOf("""{"planUsage":{"limit":2000},"spendLimitUsage":{}}""")).Quotas[0].Used == 0, "Cursor proto3 omitted spend is zero within a known allowance");
        Reject(() => CursorProvider.Parse(JsonOf("""{"planUsage":{"limit":0}}""")), "missing Cursor denominator");
        var copilot = CopilotProvider.Parse(JsonOf("""{"quota_snapshots":{"premium_interactions":{"percent_remaining":65},"chat":{"unlimited":true}},"quota_reset_date_utc":"2030-01-02T00:00:00Z"}"""));
        Check(Math.Abs(copilot.Quotas[0].Used!.Value - 35) < 0.001, "Copilot remaining is converted to used");
        Check(copilot.Quotas[1].Used == null && copilot.Quotas[1].Note == "Unlimited", "Unlimited does not invent zero usage");
        Check(CopilotProvider.Parse(JsonOf("""{"monthly_quotas":{"chat":50},"limited_user_quotas":{"chat":10}}""")).Quotas[1].Used == 80, "Copilot Free legacy remaining/entitlement");
        Reject(() => CopilotProvider.Parse(JsonOf("{}")), "unknown Copilot shape");
        var gemini = GeminiProvider.Parse(JsonOf("""{"buckets":[{"modelId":"pro","remainingFraction":0.8,"resetTime":"2030-01-02T00:00:00Z"},{"modelId":"flash","remainingFraction":0.4}]}"""));
        Check(Math.Abs(gemini.Quotas[0].Used!.Value - 60) < 0.001 && gemini.Quotas[0].Note!.Contains("pro: 80% left"), "Gemini shows tightest quota with individual model details");
        Reject(() => GeminiProvider.Parse(JsonOf("""{"buckets":[{"remainingFraction":2},{"remainingFraction":null}]}""")), "invalid or missing Gemini fraction");
        var ag = AntigravityProvider.Parse(JsonOf("""{"quota":{"gemini-weekly":{"remaining_fraction":0.25,"reset_time":"2030-01-02T00:00:00Z"}},"email":"private@example.invalid","cwd":"private-path"}"""));
        Check(ag.Quotas[0].Used == 75 && !JsonSerializer.Serialize(ag).Contains("private"), "Antigravity stores quota only, excluding account/workspace data");
        var settings = AntigravityProvider.ConnectSettings("""{"other":{"keep":true}}""", "C:\\Apps\\Widget.exe");
        Check(settings["other"]?["keep"]?.GetValue<bool>() == true && settings["statusLine"]?["command"]?.GetValue<string>().Contains("--capture-antigravity") == true, "Status line setup preserves unrelated settings");
        try { AntigravityProvider.ConnectSettings("""{"statusLine":{"command":"existing"}}""", "/app/widget"); throw new Exception("Custom status line overwritten"); } catch (InvalidOperationException) { count++; }
        var kimi = CodingPlanProviders.ParseKimi(JsonOf("""{"usage":{"limit":100,"remaining":60},"limits":[{"window":{"duration":300,"timeUnit":"TIME_UNIT_MINUTE"},"detail":{"limit":20,"used":5,"resetTime":"2030-01-02T00:00:00Z"}}]}"""));
        Check(kimi.Quotas[0].Used == 25 && kimi.Quotas[1].Used == 40, "Kimi windows and used/remaining variants");
        Reject(() => CodingPlanProviders.ParseKimi(JsonOf("""{"usage":{"limit":100}}""")), "Kimi missing used/remaining");
        var glm = CodingPlanProviders.ParseGlm(JsonOf("""{"success":true,"data":{"limits":[{"type":"TOKENS_LIMIT","unit":3,"number":5,"percentage":20},{"type":"TOKENS_LIMIT","unit":6,"number":1,"percentage":40},{"type":"TIME_LIMIT","percentage":99}]}}"""));
        Check(glm.Quotas[0].Used == 20 && glm.Quotas[1].Used == 40, "GLM separates rolling windows from MCP allowance");
        Reject(() => CodingPlanProviders.ParseGlm(JsonOf("""{"success":false,"data":{"limits":[{"type":"TOKENS_LIMIT","percentage":20}]}}""")), "GLM HTTP-200 API rejection");
        var mini = CodingPlanProviders.ParseMiniMax(JsonOf("""{"base_resp":{"status_code":0},"model_remains":[{"model_name":"general","current_interval_remaining_percent":72,"current_weekly_remaining_percent":40,"current_interval_usage_count":999,"end_time":1893542400000},{"model_name":"speech-hd","current_interval_remaining_percent":1}]}"""));
        Check(Math.Abs(mini.Quotas[0].Used!.Value - 28) < 0.001 && mini.Quotas[1].Used == 60, "MiniMax authoritative percentages override ambiguous counts and unrelated speech models");
        Reject(() => CodingPlanProviders.ParseMiniMax(JsonOf("""{"model_remains":[{"model_name":"general","current_interval_usage_count":5,"current_interval_total_count":100}]}""")), "ambiguous legacy MiniMax counts");
        Reject(() => CodingPlanProviders.ParseMiniMax(JsonOf("""{"base_resp":{"status_code":1004}}""")), "MiniMax API rejection");
        using var client = new HttpClient(new Handler(request =>
        {
            Check(request.RequestUri!.AbsoluteUri == "https://usage.example.invalid/quota" && request.Headers.Authorization?.Parameter == "synthetic-only", "Authenticated request has a fixed destination");
            Check(request.Headers.GetValues("anthropic-beta").Single() == "oauth-2025-04-20", "Claude beta header");
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }));
        await ProviderApi.Request(client, "Test", "https://usage.example.invalid/quota", "synthetic-only", default, beta: "oauth-2025-04-20");
        using var denied = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("private credential body") }));
        try { await ProviderApi.Request(denied, "Test", "https://usage.example.invalid/denied", "synthetic-only", default); throw new Exception("Forbidden accepted"); }
        catch (InvalidOperationException ex) { Check(!ex.Message.Contains("private") && ex.Message.Contains("Sign in"), "HTTP errors do not expose response bodies"); }
        var calls = 0;
        using var limited = new HttpClient(new Handler(_ => { calls++; return new(HttpStatusCode.TooManyRequests); }));
        for (var i = 0; i < 2; i++) try { await ProviderApi.Request(limited, "Test", "https://usage.example.invalid/throttled", "synthetic-only", default); } catch (InvalidOperationException) { }
        Check(calls == 1, "Rate limits back off instead of polling repeatedly");
        if (OperatingSystem.IsWindows())
        {
            var id = "test-" + Guid.NewGuid().ToString("N");
            try
            {
                CredentialVault.Save(id, "synthetic-only"); Check(CredentialVault.Read(id) == "synthetic-only", "Native credential store round trip");
                CredentialVault.Save(id, "synthetic-replacement"); Check(CredentialVault.Read(id) == "synthetic-replacement", "Native credential update");
            }
            finally { CredentialVault.Save(id, null); }
            Check(CredentialVault.Read(id) == null, "Forgotten native credential is removed");
        }
        Console.WriteLine($"PASS: {count} additional provider and connection checks");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
