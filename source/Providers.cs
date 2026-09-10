using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UsageWidget;

internal record Quota(string Label, double? Used, DateTimeOffset? Reset, string? Note = null);
internal record Reading(string Provider, Quota[] Quotas, string Balance, DateTimeOffset Fetched, int? FreeResets = null);

internal static class Json
{
    public static JsonElement Get(JsonElement e, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var name in names) if (e.TryGetProperty(name, out var v)) return v;
        return default;
    }
    public static string? Str(JsonElement e, params string[] names) => Text(Get(e, names));
    public static string? Text(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    public static double? Num(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n) && double.IsFinite(n)) return n;
        if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && double.IsFinite(n)) return n;
        return null;
    }
    public static double? Num(JsonElement e, params string[] names) => Num(Get(e, names));
    public static DateTimeOffset? Date(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (Num(e) is { } n && n > 0 && n < 253402300799)
            return DateTimeOffset.FromUnixTimeSeconds((long)n);
        if (Num(e) is { } ms && ms >= 1_000_000_000_000 && ms < 253402300799000)
            return DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
        return null;
    }
    public static double? Money(JsonElement e, params string[] names)
    {
        var v = Get(e, names);
        if (v.ValueKind == JsonValueKind.Object)
        {
            var value = Get(v, "val", "value");
            // Grok's proto3 Cent message omits val when it is zero.
            return value.ValueKind == JsonValueKind.Undefined ? 0 : Num(value);
        }
        return Num(v);
    }
}

internal static class CodexProvider
{
    public static string FindBinary()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir.Trim('"'), "codex.exe");
            if (File.Exists(path)) return path;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(root))
        {
            var found = Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (found != null) return found;
        }
        throw new InvalidOperationException("Codex CLI not found. Install or update Codex.");
    }

    public static async Task<Reading> Read(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        var ct = deadline.Token;
        using var process = new Process { StartInfo = new ProcessStartInfo(FindBinary())
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        }};
        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--stdio");
        process.Start();
        // Drain stderr without logging local configuration or account details.
        var drain = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"ai_usage_widget\",\"version\":\"1.0.0\"}}}".AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            await Response(process, 1, ct);
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}".AsMemory(), ct);
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}".AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            return Parse(await Response(process, 2, ct));
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await drain; } catch { }
        }
    }

    private static async Task<JsonElement> Response(Process p, int id, CancellationToken ct)
    {
        while (await p.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith('{')) continue;
            using var doc = JsonDocument.Parse(line);
            if (Json.Num(doc.RootElement, "id") != id) continue;
            if (Json.Get(doc.RootElement, "error").ValueKind == JsonValueKind.Object)
                throw new InvalidOperationException("Codex could not read limits. Check your Codex login.");
            return Json.Get(doc.RootElement, "result").Clone();
        }
        throw new InvalidOperationException("Codex usage connection ended. Try Refresh.");
    }

    internal static Reading Parse(JsonElement root)
    {
        var bucket = Json.Get(Json.Get(root, "rateLimitsByLimitId"), "codex");
        if (bucket.ValueKind != JsonValueKind.Object) bucket = Json.Get(root, "rateLimits");
        if (bucket.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Codex returned no usage limits.");
        var windows = new[] { Json.Get(bucket, "primary"), Json.Get(bucket, "secondary") };
        Quota Window(int mins, string name)
        {
            var w = windows.FirstOrDefault(x => Json.Num(x, "windowDurationMins") == mins);
            return new(name, Clamp(Json.Num(w, "usedPercent")), Json.Date(Json.Get(w, "resetsAt")));
        }
        var credits = Json.Get(bucket, "credits");
        var balance = Json.Num(credits, "balance");
        var creditText = Json.Get(credits, "unlimited").ValueKind == JsonValueKind.True ? "Unlimited credits"
            : balance is { } b ? $"Extra credits  {b:0.##}" : "Extra credits  unavailable";
        var available = Json.Num(Json.Get(root, "rateLimitResetCredits"), "availableCount");
        // The detail list may be capped or absent; availableCount is authoritative.
        int? freeResets = available is >= 0 and <= int.MaxValue && available == Math.Truncate(available.Value)
            ? (int)available.Value : null;
        return new("Codex", [Window(300, "5 hours"), Window(10080, "Weekly")], creditText, DateTimeOffset.Now, freeResets);
    }
    internal static double? Clamp(double? n) => n is { } value ? Math.Clamp(value, 0, 100) : null;
}

internal sealed class GrokProvider : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private string? access, refresh, clientId, userId;
    private DateTimeOffset expiry;
    private DateTime fileVersion;

    public async Task<Reading> Read(CancellationToken ct)
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("GROK_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok"), "auth.json");
        if (!File.Exists(path)) throw new InvalidOperationException("Grok login missing. Run grok login.");
        var version = File.GetLastWriteTimeUtc(path);
        if (access == null || version != fileVersion)
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
            var entry = doc.RootElement.EnumerateObject().Select(p => p.Value)
                .Where(e => Json.Str(e, "access_token", "accessToken") != null || (Json.Str(e, "auth_mode") == "oidc" && Json.Str(e, "key") != null))
                .OrderByDescending(e => Json.Date(Json.Get(e, "expires_at", "expiresAt"))).FirstOrDefault();
            access = Json.Str(entry, "access_token", "accessToken", "key");
            if (access == null) throw new InvalidOperationException("Grok OAuth login missing. Run grok login.");
            refresh = Json.Str(entry, "refresh_token", "refreshToken");
            clientId = Json.Str(entry, "oidc_client_id", "oidcClientId");
            userId = Json.Str(entry, "user_id", "userId", "principal_id");
            expiry = Json.Date(Json.Get(entry, "expires_at", "expiresAt")) ?? DateTimeOffset.MaxValue;
            fileVersion = version;
        }
        if (expiry <= DateTimeOffset.UtcNow.AddSeconds(90)) await Refresh(ct);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://cli-chat-proxy.grok.com/v1/billing?format=credits");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            req.Headers.Add("X-XAI-Token-Auth", "xai-grok-cli");
            req.Headers.Add("x-grok-client-mode", "cli");
            if (userId != null) req.Headers.Add("x-user-id", userId);
            using var response = await http.SendAsync(req, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { await Refresh(ct); continue; }
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Grok usage HTTP {(int)response.StatusCode}. Check Grok login.");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return Parse(doc.RootElement);
        }
        throw new InvalidOperationException("Grok login expired. Run grok login.");
    }

    private async Task Refresh(CancellationToken ct)
    {
        if (refresh == null || clientId == null) throw new InvalidOperationException("Grok login expired. Run grok login.");
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = clientId });
        using var response = await http.PostAsync("https://auth.x.ai/oauth2/token", body, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Grok login refresh failed. Run grok login.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        access = Json.Str(doc.RootElement, "access_token") ?? throw new InvalidOperationException("Grok login response invalid.");
        refresh = Json.Str(doc.RootElement, "refresh_token") ?? refresh;
        expiry = DateTimeOffset.UtcNow.AddSeconds(Json.Num(doc.RootElement, "expires_in") ?? 300);
        // Keep refreshed credentials in memory. The CLI owns auth.json.
    }

    internal static Reading Parse(JsonElement root)
    {
        var c = Json.Get(root, "config");
        if (c.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Grok returned no billing configuration.");
        var period = Json.Get(c, "currentPeriod", "current_period");
        var type = Json.Str(period, "type") ?? "";
        var weekly = type.Contains("WEEKLY", StringComparison.OrdinalIgnoreCase);
        var used = Json.Num(c, "creditUsagePercent", "credit_usage_percent");
        string? note = null;
        // Match Grok Build's official credit_balance_from_config behavior only for
        // a valid unified weekly response; never substitute the monthly endpoint.
        if (used == null && weekly && Json.Get(c, "isUnifiedBillingUser", "is_unified_billing_user").ValueKind == JsonValueKind.True
            && Json.Date(Json.Get(period, "end")) != null)
        {
            used = 0;
            note = "Grok omits the percentage; displayed as 0% used, matching the official Grok CLI.";
        }
        var reset = Json.Date(Json.Get(period, "end")) ?? Json.Date(Json.Get(c, "billingPeriodEnd", "billing_period_end"));
        var cents = Json.Money(c, "prepaidBalance", "prepaid_balance");
        var balance = cents is { } b ? $"Extra credits  US${Math.Abs(b) / 100:0.00}" : "Extra credits  unavailable";
        return new("Grok", [new(weekly ? "Weekly" : "Current period", CodexProvider.Clamp(used), reset, note)], balance, DateTimeOffset.Now);
    }
    public void Dispose() => http.Dispose();
}
