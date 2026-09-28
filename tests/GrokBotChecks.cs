using System.Net;
using System.Text.Json;
using UsageWidget;

internal static class GrokBotChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        JsonElement JsonOf(string json) => JsonDocument.Parse(json).RootElement.Clone();
        void Reject(string json)
        {
            try { GrokBotProvider.Parse(JsonOf(json), default); throw new Exception("Invalid usage was accepted"); }
            catch (InvalidOperationException) { count++; }
        }
        var weekly = JsonOf("""{"usagePercent":61.25,"nextResetTimestampUtc":"2030-01-02T03:04:05Z"}""");
        var reading = GrokBotProvider.Parse(weekly, JsonOf("""{"spendLimitUsage":{"individualLimit":4000}}"""));
        Check(reading.Quotas.Single().Used == 61.25 && 100 - reading.Quotas[0].Used == 38.75, "Bot usage retains used semantics for the shared percentage toggle");
        Check(reading.Quotas[0].Reset == DateTimeOffset.Parse("2030-01-02T03:04:05Z"), "Bot reset timestamp preserves UTC");
        Check(reading.Balance == "On-demand  US$0.00 / $40.00", "Proto3 omitted spending is zero cents");
        Check(GrokBotProvider.Parse(weekly, JsonOf("""{"spendLimitUsage":{"individualUsed":1250,"individualLimit":4000}}""")).Balance == "On-demand  US$12.50 / $40.00", "Spending converts cents to dollars");
        Check(GrokBotProvider.Parse(weekly, default).Balance == "On-demand unavailable", "Spending failure does not invent a balance");
        Check(GrokBotProvider.Parse(JsonOf("""{"usagePercent":120}"""), default).Quotas[0].Used == 100, "Overage leaves zero remaining");
        Check(GrokBotProvider.Parse(JsonOf("""{"usagePercent":0}"""), default).Quotas[0].Used == 0, "Explicit zero usage is valid");
        Reject("{}"); Reject("{\"usagePercent\":-1}"); Reject("{\"usagePercent\":\"NaN\"}");
        Reject("{\"usagePercent\":10,\"usesPooledEnterpriseAllowance\":true}");
        JsonElement Store(string active) => JsonOf(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["cursor-accounts"] = JsonSerializer.Serialize(new { active, accounts = new Dictionary<string, object>
            {
                ["one"] = new Dictionary<string,string> { ["cursor-access-token"] = "scoped:v1:one:first" },
                ["two"] = new Dictionary<string,string> { ["cursor-access-token"] = "second", ["cursor-selected-team-id"] = "team" }
            } })
        }));
        Check(GrokBotProvider.ReadAccount(Store("one"), x => x).Access == "first", "Reads the active scoped account");
        Check(GrokBotProvider.ReadAccount(Store("two"), x => x) == ("second", "team"), "Account switch follows the selected account and team");
        try { GrokBotProvider.ReadAccount(Store("missing"), x => x); throw new Exception("Missing account accepted"); }
        catch (InvalidOperationException) { count++; }
        using var client = new HttpClient(new Handler(request =>
        {
            Check(request.RequestUri!.Host == "api2.cursor.sh" && request.RequestUri.AbsolutePath.EndsWith("/GetSandUsageStatus"), "Usage request targets the dashboard method");
            Check(request.Method == HttpMethod.Post && request.Headers.Authorization?.Parameter == "synthetic-token", "Usage request authenticates in headers");
            Check(request.Headers.GetValues("x-cursor-team-id").Single() == "7", "Team selection accompanies request");
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"usagePercent\":25}") };
        }));
        Check((await GrokBotProvider.Request(client, "GetSandUsageStatus", "synthetic-token", "7", default)).GetProperty("usagePercent").GetDouble() == 25, "Usage response parses");
        using var denied = new HttpClient(new Handler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive body") }));
        try { await GrokBotProvider.Request(denied, "GetSandUsageStatus", "synthetic-token", null, default); throw new Exception("Expired login accepted"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("renew") && !ex.Message.Contains("sensitive"), "Expired login has safe actionable error"); }
        Console.WriteLine($"PASS: {count} Grok Bot checks");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
