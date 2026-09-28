using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UsageWidget;

internal static class ProviderApi
{
    internal static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly Dictionary<string, DateTimeOffset> Cooldowns = new();
    internal static async Task<JsonElement> Request(HttpClient client, string name, string url, string credential, CancellationToken ct,
        object? body = null, bool rawAuthorization = false, string? beta = null, string? team = null)
    {
        lock (Cooldowns)
            if (Cooldowns.TryGetValue(url, out var until) && until > DateTimeOffset.UtcNow)
                throw new InvalidOperationException($"{name} asked to slow down. Refresh after {until.LocalDateTime:t}.");
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", rawAuthorization ? credential : "Bearer " + credential);
        request.Headers.UserAgent.ParseAdd("AIUsageWidget/" + Program.AppVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (beta != null) request.Headers.Add("anthropic-beta", beta);
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            request.Headers.Add("Connect-Protocol-Version", "1");
        }
        if (team != null) request.Headers.Add("x-cursor-team-id", team);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var until = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5));
            lock (Cooldowns) Cooldowns[url] = until;
            throw new InvalidOperationException($"{name} is rate limited. Please wait before refreshing.");
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException($"{name} rejected this login or plan access. Sign in again in its app, or update Connections. API billing keys may not support subscription quotas.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{name} usage service returned HTTP {(int)response.StatusCode}.");
        try { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); return json.RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException($"{name} returned an unsupported usage response."); }
    }
    internal static double? Percent(double? number) => number is >= 0 && double.IsFinite(number.Value) ? Math.Clamp(number.Value, 0, 100) : null;
    internal static double? UsedFromRemaining(double? remaining, double scale = 100) => remaining is >= 0 && remaining <= scale ? (1 - remaining.Value / scale) * 100 : null;
    internal static double? Ratio(double? used, double? limit) => used is >= 0 && limit is > 0 ? Percent(used / limit * 100) : null;
    internal static JsonElement[] Items(JsonElement element) => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToArray() : [];
    internal static Reading Result(string name, IEnumerable<Quota> quotas, string balance)
    {
        var rows = quotas.ToArray();
        if (!rows.Any(q => q.Used != null || q.Note == "Unlimited"))
            throw new InvalidOperationException($"{name} did not return a supported quota. Check your plan and Connections; no usage is estimated.");
        return new(name, rows, balance, DateTimeOffset.Now);
    }
    internal static Quota Tightest(string label, IEnumerable<Quota> quotas)
    {
        var rows = quotas.Where(q => q.Used != null).ToArray();
        var first = rows.OrderByDescending(q => q.Used).FirstOrDefault();
        var notes = string.Join("\n", rows.Select(q => $"{q.Label}: {100-q.Used:0}% left" + (q.Reset is { } reset ? $" · resets {reset.LocalDateTime:g}" : "")));
        return new(label, first?.Used, first?.Reset, "Shows the most used reported quota, not a sum or average.\n" + notes);
    }
    internal static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    internal static async Task<JsonElement> ReadJson(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;
        try { using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct)); return doc.RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException("The provider login file could not be read. Sign in again in its app."); }
    }
    internal static async Task<string?> Saved(string id) => await Task.Run(() => CredentialVault.Read(id));
    internal static string Require(string? token, string instruction)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl)) throw new InvalidOperationException(instruction);
        return token;
    }
    internal static async Task<string> Run(string executable, string[] arguments, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token); var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token); await error;
            if (process.ExitCode != 0) throw new InvalidOperationException("The provider CLI could not read its login. Sign in again.");
            return (await output).Trim();
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    }
}
