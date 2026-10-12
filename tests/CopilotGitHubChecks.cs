using System.Net;
using System.Text.Json;
using UsageWidget;

internal static class CopilotGitHubChecks
{
    internal static async Task Run()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        JsonElement Data(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
        void Reject(Action action, string name) { try { action(); throw new Exception("FAIL: " + name); } catch (InvalidOperationException) { count++; } }
        const string secret = "synthetic-git-credential";
        var login = CopilotGitHub.ParseCredential("protocol=https\r\nhost=github.com\r\nusername=work-example\r\npassword=" + secret + "\r\n");
        Check(login.Token == secret && !login.ToString().Contains(secret) && !JsonSerializer.Serialize(login).Contains(secret), "Git credentials stay in memory and are excluded from serialization and ToString");
        Reject(() => CopilotGitHub.ParseCredential("protocol=https\nhost=untrusted.example\npassword=" + secret), "Credentials from another host are rejected");
        Reject(() => CopilotGitHub.ParseCredential("protocol=http\nhost=github.com\npassword=" + secret), "Unsafe protocol is rejected");
        Reject(() => CopilotGitHub.ParseCredential("protocol=https\nhost=github.com\n"), "Missing credentials require sign-in");
        Reject(() => CopilotGitHub.CredentialInput("work-example\npassword=other"), "Account text cannot inject credential protocol fields");
        Check(CopilotGitHub.CredentialInput("work-example") == "protocol=https\nhost=github.com\nusername=work-example\n\n", "Pinned account is supplied through stdin rather than command arguments");
        var passive = CopilotGitHub.CredentialProcess("work-example", false);
        Check(passive.Environment["GCM_INTERACTIVE"] == "false" && passive.Environment["GIT_TERMINAL_PROMPT"] == "0", "Refresh cannot open an authentication prompt");
        Check(passive.Environment["GCM_TRACE_SECRETS"] == "0" && passive.Environment["GIT_TRACE"] == "0" && passive.RedirectStandardError, "Child trace output cannot expose credentials");
        Check(CopilotGitHub.CredentialProcess(null, true).Environment["GCM_INTERACTIVE"] == "auto", "Explicit connection permits GitHub's normal sign-in UI without forcing a new login");
        Check(CopilotGitHub.Account(Data("""{"login":"work-example","id":42}"""), "WORK-EXAMPLE", 42).Account == "work-example", "Account verification uses GitHub identity and handles case");
        Check(CopilotGitHub.ValidAccount("work_example") && !CopilotGitHub.ValidAccount("work@example.invalid"), "Account protocol accepts managed usernames but excludes email addresses");
        Reject(() => CopilotGitHub.Account(Data("""{"login":"personal-example","id":42}"""), "work-example"), "Another cached account cannot replace the selected work login");
        Reject(() => CopilotGitHub.Account(Data("""{"login":"work-example","id":43}"""), "work-example", 42), "Numeric account ID prevents a reused username from changing identity");
        var id = Guid.NewGuid().ToString();
        var choice = Data(JsonSerializer.Serialize(new { Mode = "direct", Auth = "gcm", Id = id }));
        Check(CopilotGitHub.SameChoice(choice, choice), "Same direct selection accepts the result");
        Check(!CopilotGitHub.SameChoice(choice, Data(JsonSerializer.Serialize(new { Mode = "vscode", Auth = "gcm", Id = id }))), "Source changes invalidate an in-flight direct response");
        var calls = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            calls++;
            Check(request.Headers.Authorization?.Parameter == secret, "Read-only GitHub requests use the in-memory credential");
            var identity = request.RequestUri!.AbsoluteUri == "https://api.github.com/user";
            Check(identity || request.RequestUri.AbsoluteUri == "https://api.github.com/copilot_internal/user", "Only fixed GitHub endpoints receive the credential");
            return new(HttpStatusCode.OK) { Content = new StringContent(identity ? """{"login":"work-example","id":42,"email":"private-fixture"}""" :
                """{"token_based_billing":true,"copilot_plan":"business","quota_reset_date_utc":"2026-11-01T00:00:00Z","quota_snapshots":{"premium_interactions":{"percent_remaining":61.6,"quota_remaining":10790,"entitlement":17500,"credits_used":6710,"quota_reset_at":0,"unlimited":false}}}""") };
        }));
        var fetched = await CopilotGitHub.Fetch(client, login, "work-example", 42, default);
        Check(calls == 2 && Math.Abs(fetched.Usage.Quotas[0].Used!.Value - 38.4) < 0.001, "GitHub-reported percentage is authoritative for the actual dashboard shape");
        Check(fetched.Usage.Quotas[0].Reset == DateTimeOffset.Parse("2026-11-01T00:00:00Z"), "Zero reset sentinel uses the monthly reset date");
        var safe = JsonSerializer.Serialize(fetched.Usage);
        Check(safe.Contains("work-example") && safe.Contains("directly from GitHub") && !safe.Contains(secret) && !safe.Contains("private-fixture"), "Provider reading includes account/source without credential or unrelated API fields");
        Check(ConnectionDialog.Instructions("copilot").Contains("directly") && !ConnectionDialog.Instructions("copilot").Contains("keep it open"), "Connection guidance explains direct GitHub usage");
        var rejectedCalls = 0;
        using var wrongAccount = new HttpClient(new Handler(_ => { rejectedCalls++; return new(HttpStatusCode.OK) { Content = new StringContent("""{"login":"personal-example","id":43}""") }; }));
        try { await CopilotGitHub.Fetch(wrongAccount, login, "work-example", 42, default); throw new Exception("Wrong account accepted"); }
        catch (InvalidOperationException) { Check(rejectedCalls == 1, "A mismatched login stops before reading another account's quota"); }
        Console.WriteLine($"PASS: {count} direct GitHub Copilot connection checks.");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
