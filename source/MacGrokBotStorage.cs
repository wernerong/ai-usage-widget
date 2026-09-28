using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace UsageWidget;

internal sealed class MacKeychainException(string message) : InvalidOperationException(message);

internal static class MacGrokBotStorage
{
    // Electron's synchronous safeStorage: PBKDF2-SHA1, AES-128-CBC, v10 prefix.
    // This is the storage format, not a new choice of encryption for widget data.
    // https://github.com/chromium/chromium/blob/138.0.7204.0/components/os_crypt/sync/os_crypt_mac.mm
    internal static byte[] DeriveKey(string password) =>
        Rfc2898DeriveBytes.Pbkdf2(password, "saltysalt"u8, 1003, HashAlgorithmName.SHA1, 16);

    internal static async Task<byte[]> ReadKey(CancellationToken token) =>
        DeriveKey(await ReadPassword("Grok Bot Safe Storage", "Grok Bot", token));

    internal static async Task<string> ReadPassword(string service, string account, CancellationToken token, string? testKeychain = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        } };
        foreach (var argument in new[] { "find-generic-password", "-w", "-s", service, "-a", account })
            process.StartInfo.ArgumentList.Add(argument);
        if (testKeychain != null) process.StartInfo.ArgumentList.Add(testKeychain);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var password = (await output).TrimEnd('\r', '\n');
            await error; // Never forward Keychain output to logs, diagnostics, or the UI.
            if (process.ExitCode != 0 || password.Length == 0)
                throw new MacKeychainException("Unlock your login Keychain and allow access to Grok Bot Safe Storage, then refresh. Open Grok Bot and sign in if the item is missing.");
            return password;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new MacKeychainException("Keychain access timed out. Allow access to Grok Bot Safe Storage, then refresh.");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
        }
    }

    internal static string Decrypt(string value, byte[] key)
    {
        var data = Convert.FromBase64String(value);
        if (data.Length < 19 || !data.AsSpan().StartsWith("v10"u8) || (data.Length - 3) % 16 != 0)
            throw new CryptographicException("Unsupported Grok Bot encrypted login format.");
        using var aes = Aes.Create();
        aes.Key = key;
        var plain = aes.DecryptCbc(data.AsSpan(3), "                "u8, PaddingMode.PKCS7);
        try { return new UTF8Encoding(false, true).GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
