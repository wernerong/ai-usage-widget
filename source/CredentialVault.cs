using System.Runtime.InteropServices;
using System.Text;

namespace UsageWidget;

// Store only credentials explicitly entered in Connections. Existing app logins are read separately.
internal static class CredentialVault
{
    private const string Prefix = "AI Usage Widget/";
    public static string? Read(string id, IntPtr keychain = default) => OperatingSystem.IsWindows() ? ReadWindows(Prefix + id) : ReadMac(Prefix + id, "provider", keychain);
    public static void Save(string id, string? value, IntPtr keychain = default)
    {
        if (OperatingSystem.IsWindows())
        {
            if (string.IsNullOrEmpty(value)) { CredDelete(Prefix + id, 1, 0); return; }
            var data = Encoding.UTF8.GetBytes(value);
            var ptr = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, ptr, data.Length);
                var entry = new Credential { Type = 1, TargetName = Prefix + id, UserName = "provider", Persist = 2, CredentialBlobSize = (uint)data.Length, CredentialBlob = ptr };
                if (!CredWrite(ref entry, 0)) throw new InvalidOperationException("Windows Credential Manager could not save this connection.");
            }
            finally { for (var i = 0; i < data.Length; i++) Marshal.WriteByte(ptr, i, 0); Marshal.FreeHGlobal(ptr); Array.Clear(data); }
            return;
        }
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("Secure connections require Windows or macOS.");
        var service = Encoding.UTF8.GetBytes(Prefix + id); var account = "provider"u8.ToArray();
        var status = SecKeychainFindGenericPassword(keychain, service.Length, service, account.Length, account, out _, out var old, out var item);
        if (old != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, old);
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        try
        {
            if (string.IsNullOrEmpty(value)) { if (status == 0) status = SecKeychainItemDelete(item); else if (status == -25300) status = 0; }
            else if (status == 0) status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, bytes.Length, bytes);
            else if (status == -25300) status = SecKeychainAddGenericPassword(keychain, service.Length, service, account.Length, account, bytes.Length, bytes, out item);
            if (status != 0) throw new InvalidOperationException("Keychain could not save this connection. Unlock your login Keychain and allow access.");
        }
        finally { Array.Clear(bytes); if (item != IntPtr.Zero) CFRelease(item); }
    }
    internal static string? ReadWindows(string target)
    {
        if (!CredRead(target, 1, 0, out var ptr))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new InvalidOperationException("Windows Credential Manager access failed.");
        }
        try { var entry = Marshal.PtrToStructure<Credential>(ptr); return Marshal.PtrToStringUTF8(entry.CredentialBlob, (int)entry.CredentialBlobSize); }
        finally { CredFree(ptr); }
    }
    internal static string? ReadMac(string serviceName, string? accountName = null, IntPtr keychain = default)
    {
        var service = Encoding.UTF8.GetBytes(serviceName); var account = Encoding.UTF8.GetBytes(accountName ?? "");
        var status = SecKeychainFindGenericPassword(keychain, service.Length, service, account.Length, account, out var length, out var data, out var item);
        try
        {
            if (status == -25300) return null;
            if (status != 0) throw new InvalidOperationException("Keychain access was denied or locked. Unlock your login Keychain and allow this provider connection.");
            return Marshal.PtrToStringUTF8(data, length);
        }
        finally { if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName, Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, int serviceLength, byte[] service, int accountLength, byte[] account, out int length, out IntPtr data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, int serviceLength, byte[] service, int accountLength, byte[] account, int length, byte[] data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, int length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
}
