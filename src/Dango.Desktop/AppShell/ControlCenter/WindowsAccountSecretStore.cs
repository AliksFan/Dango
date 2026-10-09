using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Dango.Client.Sync.Accounts;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Per-user Windows credentials; no password or authentication data is stored in ordinary files.</summary>
internal sealed class WindowsAccountSecretStore : IAccountSecretStore
{
    private const int MaximumBlobBytes = 2560;
    private const int NotFound = 1168;

    internal static string Target(Uri origin, string slot)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != "https" || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || slot is not ("session" or "registration")) throw new IOException("secure_storage_target_invalid");
        var canonical = origin.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return $"Dango/EmailBackend/v1/{hash}/{slot}";
    }

    public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var target = Target(origin, slot);
        if (!CredRead(target, 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == NotFound) return Task.FromResult<byte[]?>(null);
            throw new IOException("secure_storage_read_failed");
        }
        try
        {
            var stored = Marshal.PtrToStructure<Credential>(pointer);
            if (stored.BlobSize is 0 or > MaximumBlobBytes || stored.Blob == IntPtr.Zero)
                throw new IOException("secure_storage_invalid");
            var bytes = new byte[stored.BlobSize];
            Marshal.Copy(stored.Blob, bytes, 0, bytes.Length);
            return Task.FromResult<byte[]?>(bytes);
        }
        finally { CredFree(pointer); }
    }

    public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var target = Target(origin, slot);
        if (value.Length is 0 or > MaximumBlobBytes) throw new IOException("secure_storage_size_invalid");
        var pointer = Marshal.AllocCoTaskMem(value.Length);
        try
        {
            Marshal.Copy(value, 0, pointer, value.Length);
            var entry = new Credential { Type = 1, TargetName = target, BlobSize = (uint)value.Length,
                Blob = pointer, Persist = 2, UserName = "Dango" };
            if (!CredWrite(ref entry, 0)) throw new IOException("secure_storage_write_failed");
        }
        finally
        {
            for (var i = 0; i < value.Length; i++) Marshal.WriteByte(pointer, i, 0);
            Marshal.FreeCoTaskMem(pointer);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CredDelete(Target(origin, slot), 1, 0) && Marshal.GetLastWin32Error() != NotFound)
            throw new IOException("secure_storage_delete_failed");
        return Task.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
