using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexMicro.Desktop.Services;

internal interface IMicroVoiceCredentialStore
{
    string? Read(string scope, string provider);

    void Write(string scope, string provider, string value);

    void Delete(string scope, string provider);
}

/// <summary>
/// Stores voice API keys in the current user's Windows Credential Manager.
/// Profile JSON contains only non-secret provider settings.
/// </summary>
internal sealed class MicroVoiceCredentialStore : IMicroVoiceCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBytes = 2_560;

    public string? Read(string scope, string provider)
    {
        var target = Target(scope, provider);
        if (!CredRead(
                target,
                CredentialTypeGeneric,
                0,
                out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string scope, string provider, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        if (bytes.Length > MaximumCredentialBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"A voice credential cannot exceed {MaximumCredentialBytes} UTF-8 bytes.");
        }

        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target(scope, provider),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public void Delete(string scope, string provider)
    {
        if (CredDelete(Target(scope, provider), CredentialTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error);
        }
    }

    internal static string Target(string scope, string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (!MicroVoiceProviders.IsKnown(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        var safeScope = new string(scope.Trim()
            .Where(character => char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_')
            .Take(64)
            .ToArray());
        if (safeScope.Length == 0)
        {
            throw new ArgumentException(
                "The keypad credential scope contains no safe characters.",
                nameof(scope));
        }

        return $"AgentController/CodexMicro/voice/{safeScope}/{provider}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
