using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageWidget;

internal static class CopilotGitHub
{
    internal static bool ValidAccount(string? account) => account != null &&
        Regex.IsMatch(account, @"^[a-zA-Z0-9](?:[a-zA-Z0-9_-]{0,37}[a-zA-Z0-9])?$");

    internal sealed class Login(string token)
    {
        internal string Token { get; } = token;
        public override string ToString() => "GitHub login (credential hidden)";
    }

    internal static Login ParseCredential(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index > 0 && !fields.TryAdd(line[..index], line[(index + 1)..].TrimEnd('\r')))
                throw new InvalidOperationException("GitHub returned an invalid credential. Reconnect your GitHub account.");
        }
        if (!fields.TryGetValue("protocol", out var protocol) || protocol != "https" ||
            !fields.TryGetValue("host", out var host) || host != "github.com")
            throw new InvalidOperationException("The GitHub credential has an unsupported host.");
        return new(ProviderApi.Require(fields.GetValueOrDefault("password"), "Connect your GitHub account through Connections → Copilot."));
    }

    internal static ProcessStartInfo CredentialProcess(string? account, bool interactive)
    {
        if (account != null && !ValidAccount(account)) throw new InvalidOperationException("Enter a valid GitHub username.");
        var binary = "git";
        if (OperatingSystem.IsWindows())
        {
            var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
            if (File.Exists(standard)) binary = standard;
        }
        else if (OperatingSystem.IsMacOS())
        {
            binary = new[] { "/opt/homebrew/bin/git", "/usr/local/bin/git", "/usr/bin/git" }.FirstOrDefault(File.Exists) ?? "git";
        }
        var info = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath() };
        info.ArgumentList.Add("credential"); info.ArgumentList.Add("fill");
        // Background refresh never opens sign-in or account-selection prompts.
        // Only the explicit Connect button permits GCM's standard approval UI.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = interactive ? "auto" : "false";
        foreach (var key in new[] { "GIT_TRACE", "GIT_TRACE_CURL", "GIT_CURL_VERBOSE", "GCM_TRACE", "GCM_TRACE_SECRETS", "GCM_TRACE_MSAUTH", "GCM_DEBUG" }) info.Environment[key] = "0";
        return info;
    }

    internal static string CredentialInput(string? account)
    {
        if (account != null && !ValidAccount(account)) throw new InvalidOperationException("Enter a valid GitHub username.");
        return "protocol=https\nhost=github.com\n" + (account == null ? "" : $"username={account}\n") + "\n";
    }

    internal static async Task<Login> Credential(string? account, bool interactive, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(interactive ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(20));
        using var process = new Process { StartInfo = CredentialProcess(account, interactive) };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteAsync(CredentialInput(account).AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token); await error;
            if (process.ExitCode != 0) throw new InvalidOperationException("Git could not read this GitHub login. Choose Connect GitHub account, or use the saved-token / GitHub CLI connection. Git Credential Manager must be configured.");
            return ParseCredential(await output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Install Git with Git Credential Manager to connect your GitHub account, or use the saved-token / GitHub CLI connection.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("GitHub sign-in took too long. Choose Connect GitHub account to try again.");
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    }

    internal static (string Account, double Id) Account(JsonElement user, string? expected, double? expectedId = null)
    {
        var account = Json.Str(user, "login"); var id = Json.Num(user, "id");
        if (!ValidAccount(account) || id is not > 0)
            throw new InvalidOperationException("GitHub did not return a valid account identity. Reconnect your GitHub account.");
        if (expected != null && !string.Equals(account, expected, StringComparison.OrdinalIgnoreCase) || expectedId != null && id != expectedId)
            throw new InvalidOperationException("The GitHub login belongs to a different account. Choose the account with your Copilot subscription in Connections → Copilot.");
        return (account!, id.Value);
    }

    internal static async Task<(Reading Usage, string Account, double Id)> Fetch(HttpClient client, Login login,
        string? expected, double? expectedId, CancellationToken ct)
    {
        var user = Account(await ProviderApi.Request(client, "GitHub", "https://api.github.com/user", login.Token, ct), expected, expectedId);
        var usage = CopilotProvider.Parse(await ProviderApi.Request(client, "Copilot", "https://api.github.com/copilot_internal/user", login.Token, ct));
        return (usage with { Quotas = usage.Quotas.Select(q => q with {
            Status = q.Status ?? (q.Note == "Unlimited" ? "Unlimited" : null),
            Note = string.Join("\n", new[] { q.Note, $"GitHub · {user.Account}", "Refreshed directly from GitHub; your editor can be closed." }.Where(s => !string.IsNullOrWhiteSpace(s))) }).ToArray() }, user.Account, user.Id);
    }

    internal static async Task Connect(string? account, CancellationToken ct)
    {
        account = string.IsNullOrWhiteSpace(account) ? null : account.Trim();
        var fetched = await Fetch(ProviderApi.Client, await Credential(account, true, ct), account, null, ct);
        // Credentials remain in the user's existing Git credential store. Only
        // the verified username, numeric account ID and source selection persist.
        await Select(fetched.Account, fetched.Id, ct);
    }

    internal static async Task Select(string account, double id, CancellationToken ct)
    {
        if (!ValidAccount(account) || !(id > 0 && double.IsFinite(id))) throw new InvalidOperationException("Invalid GitHub account.");
        Directory.CreateDirectory(Preferences.Folder);
        var temp = CopilotVSCode.ConnectionPath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new { Schema = 1, Mode = "direct", Auth = "gcm", Account = account, AccountId = id, Id = Guid.NewGuid().ToString() }), ct);
        File.Move(temp, CopilotVSCode.ConnectionPath, true);
    }

    internal static async Task<Reading> Read(JsonElement choice, CancellationToken ct)
    {
        var account = Json.Str(choice, "Account");
        if (Json.Num(choice, "Schema") != 1 || !ValidAccount(account) || !Guid.TryParse(Json.Str(choice, "Id"), out _))
            throw new InvalidOperationException("Choose your GitHub account in Connections → Copilot.");
        var fetched = await Fetch(ProviderApi.Client, await Credential(account, false, ct), account, Json.Num(choice, "AccountId"), ct);
        var current = await ProviderApi.ReadJson(CopilotVSCode.ConnectionPath, ct);
        if (!SameChoice(choice, current))
            throw new InvalidOperationException("The Copilot connection changed. Refresh to read the newly selected GitHub account.");
        return fetched.Usage;
    }
    internal static bool SameChoice(JsonElement choice, JsonElement current) =>
        Json.Str(current, "Mode") == "direct" && Json.Str(current, "Auth") == "gcm" &&
        Guid.TryParse(Json.Str(choice, "Id"), out _) && Json.Str(current, "Id") == Json.Str(choice, "Id");
}
