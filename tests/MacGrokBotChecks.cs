using System.Diagnostics;
using System.Security.Cryptography;
using UsageWidget;

internal static class MacGrokBotChecks
{
    public static async Task Run()
    {
        const string password = "synthetic-safe-storage-password";
        // Independent Python hashlib/cryptography fixture, not a real account credential.
        const string encrypted = "djEw2jCH65//kvtV/fRkUUqh2ep7mKkG9/kZ5cWdmcj2dxQ=";
        var key = MacGrokBotStorage.DeriveKey(password);
        void Check(bool valid) { if (!valid) throw new Exception("Mac Grok Bot storage check failed"); }
        Check(MacGrokBotStorage.Decrypt(encrypted, key) == "synthetic-access-token");
        foreach (var bad in new[] { "djEx", Convert.ToBase64String(new byte[19]), encrypted[..^4] })
        {
            try { MacGrokBotStorage.Decrypt(bad, key); throw new Exception("Invalid Mac ciphertext accepted"); }
            catch (CryptographicException) { }
        }
        Check(GrokBotProvider.LoginFolder(true, "/Users/test", "unused") == Path.Combine("/Users/test", "Library", "Application Support", "Grok Bot"));
        Check(GrokBotProvider.LoginFolder(false, "unused", "appdata") == Path.Combine("appdata", "Grok Bot"));
        CryptographicOperations.ZeroMemory(key);
        Console.WriteLine("PASS: 6 Mac Grok Bot storage checks");
        if (!OperatingSystem.IsMacOS()) return;

        // Only synthetic secrets in an isolated temporary keychain. Never access the user's login keychain.
        var folder = Path.Combine(Path.GetTempPath(), "widget-keychain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "fixture.keychain-db");
        try
        {
            await Security("create-keychain", "-p", "synthetic-keychain-password", path);
            await Security("unlock-keychain", "-p", "synthetic-keychain-password", path);
            await Security("add-generic-password", "-s", "Widget Test Safe Storage", "-a", "Widget Test", "-w", password, "-A", path);
            var actual = await MacGrokBotStorage.ReadPassword("Widget Test Safe Storage", "Widget Test", default, path);
            Check(actual == password);
            var nativeKey = MacGrokBotStorage.DeriveKey(actual);
            try { Check(MacGrokBotStorage.Decrypt(encrypted, nativeKey) == "synthetic-access-token"); }
            finally { CryptographicOperations.ZeroMemory(nativeKey); }
            try
            {
                await MacGrokBotStorage.ReadPassword("Missing Item", "Widget Test", default, path);
                throw new Exception("Missing Keychain item accepted");
            }
            catch (MacKeychainException ex) { Check(ex.Message.Contains("Unlock") && !ex.Message.Contains(password)); }
            Console.WriteLine("PASS: 3 native macOS Keychain checks");
        }
        finally
        {
            if (File.Exists(path)) await Security("delete-keychain", path);
            Directory.Delete(folder, true);
        }
    }

    private static async Task Security(params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0) throw new Exception("Synthetic Keychain fixture command failed: " + arguments[0]);
    }
}
