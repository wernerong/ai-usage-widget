using System.Text.Json;

namespace UsageWidget;

internal static class CodingPlanProviders
{
    internal static readonly string[] Ids = ["kimi", "glm", "glm-cn", "minimax", "minimax-cn"];
    internal static async Task<Reading> Read(string id, CancellationToken ct)
    {
        var key = ProviderApi.Require(await ProviderApi.Saved(id), "Add your coding-plan subscription API key in Connections. A pay-as-you-go API key may not expose plan usage.");
        var endpoint = id switch
        {
            "kimi" => "https://api.kimi.com/coding/v1/usages",
            "glm" => "https://api.z.ai/api/monitor/usage/quota/limit",
            "glm-cn" => "https://open.bigmodel.cn/api/monitor/usage/quota/limit",
            "minimax" => "https://www.minimax.io/v1/token_plan/remains",
            "minimax-cn" => "https://www.minimax.cn/v1/token_plan/remains",
            _ => throw new InvalidOperationException("Unsupported coding plan.")
        };
        var root = await ProviderApi.Request(ProviderApi.Client, id, endpoint, key, ct, rawAuthorization: id.StartsWith("glm"));
        return id == "kimi" ? ParseKimi(root) : id.StartsWith("glm") ? ParseGlm(root) : ParseMiniMax(root);
    }
    internal static Reading ParseKimi(JsonElement root)
    {
        Quota From(JsonElement detail, string label)
        {
            var total = Json.Num(detail, "limit");
            var used = Json.Num(detail, "used") ?? total - Json.Num(detail, "remaining");
            return new(label, ProviderApi.Ratio(used, total), Json.Date(Json.Get(detail, "resetTime", "reset_at", "resetAt", "reset_time")));
        }
        var quotas = new List<Quota> { From(Json.Get(root, "usage"), "Weekly") };
        foreach (var limit in ProviderApi.Items(Json.Get(root, "limits")))
        {
            var window = Json.Get(limit, "window"); var duration = Json.Num(window, "duration"); var unit = Json.Str(window, "timeUnit") ?? "";
            var hours = unit.Contains("MINUTE") ? duration / 60 : unit.Contains("HOUR") ? duration : unit.Contains("DAY") ? duration * 24 : duration / 3600;
            var label = hours == 5 ? "5 hours" : hours == 168 ? "Weekly" : null;
            if (label == null) continue;
            var detail = Json.Get(limit, "detail");
            var quota = From(detail.ValueKind == JsonValueKind.Object ? detail : limit, label);
            if (quota.Used != null) { quotas.RemoveAll(q => q.Label == label); quotas.Add(quota); }
        }
        return ProviderApi.Result("Kimi", new[] { "5 hours", "Weekly" }.Select(label => quotas.FirstOrDefault(q => q.Label == label) ?? new(label, null, null)), "Kimi Code subscription");
    }
    internal static Reading ParseGlm(JsonElement root)
    {
        if (Json.Get(root, "success").ValueKind == JsonValueKind.False || Json.Num(root, "code") is { } code && code != 200 && code != 0)
            throw new InvalidOperationException("GLM rejected the coding-plan key. Check the selected region and Connections.");
        var limits = ProviderApi.Items(Json.Get(Json.Get(root, "data"), "limits"));
        Quota Window(string label, int unit, int number)
        {
            var matches = limits.Where(q => Json.Str(q, "type") == "TOKENS_LIMIT" &&
                (Json.Num(q, "unit") == unit && Json.Num(q, "number") == number || label == "5 hours" && Json.Num(q, "unit") == null && Json.Num(q, "number") == null)).ToArray();
            return ProviderApi.Tightest(label, matches.Select(q => new Quota(label, ProviderApi.Percent(Json.Num(q, "percentage")), Json.Date(Json.Get(q, "nextResetTime")))));
        }
        return ProviderApi.Result("GLM", [Window("5 hours", 3, 5), Window("Weekly", 6, 1)], "GLM Coding Plan");
    }
    internal static Reading ParseMiniMax(JsonElement root)
    {
        if (Json.Num(Json.Get(root, "base_resp"), "status_code") is { } status && status != 0)
            throw new InvalidOperationException("MiniMax rejected the subscription key. Check the selected region and Connections.");
        // Counts have changed meaning across MiniMax plan generations. Use the authoritative percentage only.
        var models = ProviderApi.Items(Json.Get(root, "model_remains"));
        var coding = models.Where(m => (Json.Str(m, "model_name") ?? "").StartsWith("MiniMax", StringComparison.OrdinalIgnoreCase) || Json.Str(m, "model_name") == "general").ToArray();
        Quota Window(string label, string prefix, string reset)
        {
            return ProviderApi.Tightest(label, coding.Select(m => new Quota(Json.Str(m, "model_name") ?? label,
                ProviderApi.UsedFromRemaining(Json.Num(m, prefix + "remaining_percent")), Json.Date(Json.Get(m, reset)))));
        }
        return ProviderApi.Result("MiniMax", [Window("5 hours", "current_interval_", "end_time"), Window("Weekly", "current_weekly_", "weekly_end_time")], "MiniMax Token Plan");
    }
}

internal static class GeminiProvider
{
    internal static async Task<Reading> Read(CancellationToken ct)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") ?? ProviderApi.Home, ".gemini");
        var credentials = await ProviderApi.ReadJson(Path.Combine(folder, "oauth_creds.json"), ct);
        if (credentials.ValueKind != JsonValueKind.Object && OperatingSystem.IsMacOS())
        {
            var value = await Task.Run(() => CredentialVault.ReadMac("gemini-cli-oauth", "main-account"), ct);
            if (value != null) { using var doc = JsonDocument.Parse(value); credentials = Json.Get(doc.RootElement, "token").Clone(); }
        }
        var access = ProviderApi.Require(Json.Str(credentials, "access_token", "accessToken"), "Run Gemini CLI and choose Sign in with Google. API-key and Vertex billing do not expose this Code Assist allowance.");
        var project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT_ID");
        var setup = await ProviderApi.Request(ProviderApi.Client, "Gemini", "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist", access, ct,
            new { cloudaicompanionProject = project, metadata = new { ideType = "IDE_UNSPECIFIED", platform = "PLATFORM_UNSPECIFIED", pluginType = "GEMINI" } });
        project ??= Json.Str(setup, "cloudaicompanionProject");
        project = ProviderApi.Require(project, "Open Gemini CLI and complete Google sign-in/project setup, then refresh.");
        return Parse(await ProviderApi.Request(ProviderApi.Client, "Gemini", "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", access, ct, new { project }));
    }
    internal static Reading Parse(JsonElement root)
    {
        var quotas = ProviderApi.Items(Json.Get(root, "buckets")).Select(b => new Quota(Json.Str(b, "modelId") ?? "Model",
            ProviderApi.UsedFromRemaining(Json.Num(b, "remainingFraction"), 1), Json.Date(Json.Get(b, "resetTime"))));
        return ProviderApi.Result("Gemini", [ProviderApi.Tightest("Models", quotas)], "Gemini CLI / Code Assist");
    }
}
