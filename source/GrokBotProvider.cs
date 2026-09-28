using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageWidget;

// Read-only integration with the Windows Grok Bot desktop login. Tokens are never
// persisted or refreshed here: Grok Bot owns its login and refresh-token rotation.
internal static class GrokBotProvider
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<Reading> Read(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Grok Bot monitoring currently requires its Windows desktop app.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grok Bot");
        string access;
        string? team;
        try
        {
            using var store = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "sand-secrets.json"), token));
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "Local State"), token));
            var wrappedKey = Convert.FromBase64String(Json.Str(Json.Get(state.RootElement, "os_crypt"), "encrypted_key")!);
            if (!wrappedKey.AsSpan().StartsWith("DPAPI"u8)) throw new CryptographicException();
            var key = Unprotect(wrappedKey[5..]);
            try
            {
                (access, team) = ReadAccount(store.RootElement, value => Decrypt(value, key));
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("Open Grok Bot and sign in on this Windows account, then refresh the widget."); }

        var weekly = await Request(Client, "GetSandUsageStatus", access, team, token);
        JsonElement spending = default;
        try { spending = await Request(Client, "GetCurrentPeriodUsage", access, team, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
        { /* The weekly allowance remains useful if the spending request fails. */ }
        return Parse(weekly, spending);
    }

    internal static (string Access, string? Team) ReadAccount(JsonElement store, Func<string, string> decrypt)
    {
        using var accounts = JsonDocument.Parse(Json.Str(store, "cursor-accounts") ?? throw new InvalidOperationException());
        var active = Json.Str(accounts.RootElement, "active");
        if (string.IsNullOrEmpty(active)) throw new InvalidOperationException();
        var account = Json.Get(Json.Get(accounts.RootElement, "accounts"), active);
        string ReadSecret(string name)
        {
            var value = Json.Str(account, name) ?? throw new InvalidOperationException();
            if (value.StartsWith("scoped:v1:", StringComparison.Ordinal))
            {
                var parts = value.Split(':', 4);
                if (parts.Length != 4 || parts[2] != active) throw new InvalidOperationException();
                value = parts[3];
            }
            return decrypt(value);
        }
        var access = ReadSecret("cursor-access-token");
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException();
        var team = Json.Str(account, "cursor-selected-team-id") is null ? null : ReadSecret("cursor-selected-team-id");
        return (access, team);
    }

    internal static async Task<JsonElement> Request(HttpClient client, string method, string access, string? team, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api2.cursor.sh/aiserver.v1.DashboardService/" + method);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("Connect-Protocol-Version", "1");
        request.Headers.Add("x-ghost-mode", "true");
        if (!string.IsNullOrWhiteSpace(team)) request.Headers.Add("x-cursor-team-id", team);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Open Grok Bot to renew its login, then refresh the widget.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Grok Bot usage service returned HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.Clone();
    }

    internal static Reading Parse(JsonElement weekly, JsonElement spending)
    {
        if (Json.Get(weekly, "usesPooledEnterpriseAllowance").ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("Grok Bot uses a pooled team allowance; an individual percentage is unavailable.");
        var used = Json.Num(weekly, "usagePercent");
        if (used is null || used < 0)
            throw new InvalidOperationException("Grok Bot did not report a weekly usage percentage.");
        var reset = Json.Date(Json.Get(weekly, "nextResetTimestampUtc"));
        var balance = "On-demand unavailable";
        var spend = Json.Get(spending, "spendLimitUsage");
        if (spend.ValueKind == JsonValueKind.Object)
        {
            var limit = Json.Num(spend, "individualLimit");
            // Proto3 omits a zero individualUsed, as in the desktop app's response.
            var paid = Json.Get(spend, "individualUsed").ValueKind == JsonValueKind.Undefined ? 0 : Json.Num(spend, "individualUsed");
            if (paid is >= 0 && limit is > 0)
                balance = string.Create(CultureInfo.InvariantCulture, $"On-demand  US${paid / 100:0.00} / ${limit / 100:0.00}");
            else if (limit == 0 && paid == 0) balance = "On-demand  $0 limit";
        }
        return new("Grok Bot", [new("Weekly", Math.Clamp(used.Value, 0, 100), reset)], balance, DateTimeOffset.Now);
    }

    private static string Decrypt(string value, byte[] key)
    {
        var data = Convert.FromBase64String(value);
        byte[] plain;
        if (data.AsSpan().StartsWith("v10"u8))
        {
            if (data.Length < 31) throw new CryptographicException();
            plain = new byte[data.Length - 31];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(data.AsSpan(3, 12), data.AsSpan(15, plain.Length), data.AsSpan(data.Length - 16), plain);
        }
        else plain = Unprotect(data);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Unprotect(byte[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Length = data.Length, Data = handle.AddrOfPinnedObject() };
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output))
                throw new CryptographicException("Windows could not unlock the Grok Bot login.");
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { handle.Free(); }
    }
}
