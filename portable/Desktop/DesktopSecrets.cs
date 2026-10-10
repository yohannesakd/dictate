using System.Runtime.InteropServices;
using System.Text;
using Dictate.Core;

namespace Dictate.Desktop;

public sealed class DesktopSecrets : ISecretStore
{
    public async Task<string?> ReadAsync(string account, CancellationToken cancellation = default)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CredRead("Dictate/" + account, 1, 0, out var pointer))
            {
                if (Marshal.GetLastWin32Error() == 1168) return null;
                throw new InvalidOperationException("Windows Credential Manager could not read this credential.");
            }
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                var data = new byte[credential.BlobSize];
                Marshal.Copy(credential.Blob, data, 0, data.Length);
                return Encoding.UTF8.GetString(data);
            }
            finally { CredFree(pointer); }
        }
        var result = await ProcessCommand.RunAsync("secret-tool", ["lookup", "application", "dev.local.dictate.portable", "account", account], cancellation: cancellation).ConfigureAwait(false);
        if (result.Exit == 1 && result.Output.Length == 0) return null;
        if (result.Exit != 0) throw new InvalidOperationException("The desktop keyring could not read this credential. " + result.Output.Trim());
        return result.Output.TrimEnd('\r', '\n');
    }

    public async Task WriteAsync(string account, string? value, CancellationToken cancellation = default)
    {
        if (OperatingSystem.IsWindows())
        {
            if (value is null)
            {
                if (!CredDelete("Dictate/" + account, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new InvalidOperationException("Windows could not remove this credential.");
                return;
            }
            var data = Encoding.UTF8.GetBytes(value);
            var pointer = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, pointer, data.Length);
                var credential = new Credential { Type = 1, TargetName = "Dictate/" + account, UserName = "Dictate", Persist = 2, BlobSize = (uint)data.Length, Blob = pointer };
                if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("Windows Credential Manager could not save this credential.");
            }
            finally { Marshal.FreeHGlobal(pointer); }
            return;
        }
        var args = value is null ? new[] { "clear", "application", "dev.local.dictate.portable", "account", account }
            : new[] { "store", "--label=Dictate " + account, "application", "dev.local.dictate.portable", "account", account };
        var result = await ProcessCommand.RunAsync("secret-tool", args, value is null ? null : value + "\n", cancellation).ConfigureAwait(false);
        if (result.Exit != 0) throw new InvalidOperationException("The desktop keyring could not save this credential. " + result.Output.Trim());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public long LastWritten;
        public uint BlobSize;
        public nint Blob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32")] private static extern void CredFree(nint credential);
}
