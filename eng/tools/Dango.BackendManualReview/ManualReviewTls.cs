using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Dango.BackendManualReview;

public sealed record ManualReviewOwnedDirectory(string Path, Guid OwnerId, bool CreatedForThisRun, bool AclProtectedForCurrentUser);
public sealed record ManualReviewTlsReceipt(Guid OwnerId, string Directory, string Thumbprint, string RawSha256,
    DateTimeOffset NotBefore, DateTimeOffset NotAfter);
public sealed class ManualReviewTlsMaterial(X509Certificate2 certificate, ManualReviewTlsReceipt receipt) : IDisposable
{
    public X509Certificate2 Certificate { get; } = certificate;
    public ManualReviewTlsReceipt Receipt { get; } = receipt;
    public void Dispose() => Certificate.Dispose();
}
public interface IManualReviewTlsResources
{
    void VerifyOwnedDirectory(ManualReviewOwnedDirectory directory);
    byte[] ProtectForCurrentUser(byte[] bytes, Guid ownerId);
    void WriteNewFile(string path, byte[] bytes);
    X509Certificate2 LoadServerCertificate(byte[] pfx, string password, X509KeyStorageFlags flags);
    IReadOnlyList<X509Certificate2> FindCurrentUserRoot(string thumbprint);
    void AddCurrentUserRoot(X509Certificate2 publicCertificate);
    void RemoveCurrentUserRoot(X509Certificate2 publicCertificate);
}
public sealed class ManualReviewTls(IManualReviewTlsResources? resources = null)
{
    private readonly IManualReviewTlsResources backend = resources ?? new WindowsTlsResources();
    private readonly Dictionary<ManualReviewTlsReceipt, TrustOwnership> owned = new(ReferenceEqualityComparer.Instance);
    private readonly object gate = new();

    // A memory-only capability check; never reads a certificate store.
    public bool CanRemoveOwnedTrust(ManualReviewTlsReceipt receipt)
    {
        lock (gate) return owned.TryGetValue(receipt, out var ownership) && ownership.AddAttempted && !ownership.Removed;
    }

    public ManualReviewTlsMaterial Prepare(bool authorizationVerified, ManualReviewOwnedDirectory directory, DateTimeOffset now)
    {
        ManualReviewCredentials.RequireAuthorization(authorizationVerified);
        if (!directory.CreatedForThisRun || !directory.AclProtectedForCurrentUser || directory.OwnerId == Guid.Empty
            || !Path.IsPathFullyQualified(directory.Path)) throw new InvalidDataException("manual_tls_directory_unproven");
        lock (gate)
        {
            if (owned.Keys.Any(receipt => receipt.OwnerId == directory.OwnerId)) throw new InvalidOperationException("manual_tls_owner_already_prepared");
            backend.VerifyOwnedDirectory(directory);
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest($"CN=Dango manual review {Guid.NewGuid():N}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());
            var start = now.AddMinutes(-5);
            using var certificate = request.CreateSelfSigned(start, start.AddDays(3));
            X509Certificate2? serverCertificate = null;
            try
            {
                ValidateCertificate(certificate);
                var receipt = new ManualReviewTlsReceipt(directory.OwnerId, Path.GetFullPath(directory.Path), certificate.Thumbprint,
                    Convert.ToHexString(SHA256.HashData(certificate.RawData)), new(certificate.NotBefore.ToUniversalTime()), new(certificate.NotAfter.ToUniversalTime()));
                var passwordBytes = RandomNumberGenerator.GetBytes(48);
                byte[]? exported = null;
                try
                {
                    var password = Convert.ToBase64String(passwordBytes);
                    var passwordUtf8 = Encoding.UTF8.GetBytes(password);
                    byte[] encrypted;
                    try { encrypted = backend.ProtectForCurrentUser(passwordUtf8, directory.OwnerId); }
                    finally { CryptographicOperations.ZeroMemory(passwordUtf8); }
                    exported = certificate.Export(X509ContentType.Pfx, password);
                    // Reload precisely this newly generated PFX. Schannel cannot serve the
                    // request's ephemeral key; DefaultKeySet creates a temporary native key
                    // container, owned and removed by the returned certificate's Dispose.
                    serverCertificate = backend.LoadServerCertificate(exported, password, X509KeyStorageFlags.DefaultKeySet);
                    ValidateMatchingCertificate(serverCertificate, receipt);
                    if (!serverCertificate.HasPrivateKey) throw new InvalidDataException("manual_tls_server_private_key_missing");
                    backend.WriteNewFile(Path.Combine(directory.Path, "tls-password.dpapi"), encrypted);
                    backend.WriteNewFile(Path.Combine(directory.Path, "tls-server.pfx"), exported);
                    backend.WriteNewFile(Path.Combine(directory.Path, "tls-public.cer"), certificate.RawData);
                    backend.WriteNewFile(Path.Combine(directory.Path, "tls-created-receipt.json"), JsonSerializer.SerializeToUtf8Bytes(receipt));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passwordBytes);
                    if (exported is not null) CryptographicOperations.ZeroMemory(exported);
                }
                owned.Add(receipt, new TrustOwnership());
                return new(serverCertificate!, receipt);
            }
            catch { serverCertificate?.Dispose(); throw; }
        }
    }

    public void Trust(bool authorizationVerified, ManualReviewTlsMaterial material)
    {
        ManualReviewCredentials.RequireAuthorization(authorizationVerified);
        lock (gate)
        {
            if (!owned.TryGetValue(material.Receipt, out var ownership) || ownership.AddAttempted)
                throw new InvalidOperationException("manual_tls_ownership_unproven");
            ValidateMatchingCertificate(material.Certificate, material.Receipt);
            var existing = backend.FindCurrentUserRoot(material.Receipt.Thumbprint);
            try { if (existing.Count != 0) throw new InvalidOperationException("manual_tls_thumbprint_already_trusted"); }
            finally { DisposeAll(existing); }
            using var publicCertificate = X509CertificateLoader.LoadCertificate(material.Certificate.RawData);
            // Record the proven prior absence before Add, so ambiguous Add failures can be examined safely.
            ownership.AddAttempted = true;
            backend.AddCurrentUserRoot(publicCertificate);
            var imported = backend.FindCurrentUserRoot(material.Receipt.Thumbprint);
            try
            {
                if (imported.Count != 1) throw new InvalidOperationException("manual_tls_import_unconfirmed");
                ValidateMatchingCertificate(imported[0], material.Receipt);
                if (imported[0].HasPrivateKey) throw new InvalidOperationException("manual_tls_root_contains_private_key");
            }
            finally { DisposeAll(imported); }
        }
    }

    public void RemoveOwnedTrust(bool authorizationVerified, ManualReviewTlsReceipt receipt)
    {
        ManualReviewCredentials.RequireAuthorization(authorizationVerified);
        lock (gate)
        {
            if (!owned.TryGetValue(receipt, out var ownership) || !ownership.AddAttempted || ownership.Removed)
                throw new InvalidOperationException("manual_tls_cleanup_ownership_unproven");
            var existing = backend.FindCurrentUserRoot(receipt.Thumbprint);
            try
            {
                if (existing.Count > 1) throw new InvalidOperationException("manual_tls_cleanup_ambiguous");
                if (existing.Count == 1)
                {
                    ValidateMatchingCertificate(existing[0], receipt);
                    if (existing[0].HasPrivateKey) throw new InvalidOperationException("manual_tls_cleanup_private_key_present");
                    backend.RemoveCurrentUserRoot(existing[0]);
                }
            }
            finally { DisposeAll(existing); }
            var remaining = backend.FindCurrentUserRoot(receipt.Thumbprint);
            try { if (remaining.Count != 0) throw new InvalidOperationException("manual_tls_cleanup_unconfirmed"); }
            finally { DisposeAll(remaining); }
            ownership.Removed = true;
        }
    }

    private static void ValidateMatchingCertificate(X509Certificate2 certificate, ManualReviewTlsReceipt receipt)
    {
        ValidateCertificate(certificate);
        if (certificate.Thumbprint != receipt.Thumbprint || Convert.ToHexString(SHA256.HashData(certificate.RawData)) != receipt.RawSha256
            || new DateTimeOffset(certificate.NotBefore.ToUniversalTime()) != receipt.NotBefore
            || new DateTimeOffset(certificate.NotAfter.ToUniversalTime()) != receipt.NotAfter)
            throw new InvalidOperationException("manual_tls_certificate_identity_changed");
    }

    public static void ValidateCertificate(X509Certificate2 certificate)
    {
        try
        {
            var validity = certificate.NotAfter.ToUniversalTime() - certificate.NotBefore.ToUniversalTime();
            if (validity <= TimeSpan.Zero || validity > TimeSpan.FromDays(3) || certificate.Extensions.Count != 4
                || !certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
                throw new InvalidDataException("manual_tls_certificate_invalid");
            var basic = certificate.Extensions.OfType<X509BasicConstraintsExtension>().Single();
            var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().Single();
            var purposes = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
            if (basic.CertificateAuthority || basic.HasPathLengthConstraint || !basic.Critical
                || usage.KeyUsages != (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment)
                || purposes.EnhancedKeyUsages.Count != 1 || purposes.EnhancedKeyUsages[0].Value != "1.3.6.1.5.5.7.3.1")
                throw new InvalidDataException("manual_tls_certificate_invalid");
            var san = certificate.Extensions.Cast<X509Extension>().Single(extension => extension.Oid?.Value == "2.5.29.17");
            var reader = new AsnReader(san.RawData, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            var ip = sequence.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7));
            if (!ip.AsSpan().SequenceEqual(new byte[] { 127, 0, 0, 1 }) || sequence.HasData || reader.HasData)
                throw new InvalidDataException("manual_tls_certificate_invalid");
        }
        catch (Exception ex) when (ex is InvalidOperationException or AsnContentException or CryptographicException)
        { throw new InvalidDataException("manual_tls_certificate_invalid"); }
    }

    private static void DisposeAll(IReadOnlyList<X509Certificate2> certificates)
    {
        foreach (var certificate in certificates) certificate.Dispose();
    }
    private sealed class TrustOwnership { public bool AddAttempted; public bool Removed; }

    private sealed class WindowsTlsResources : IManualReviewTlsResources
    {
        public X509Certificate2 LoadServerCertificate(byte[] pfx, string password, X509KeyStorageFlags flags)
        {
            if (flags != X509KeyStorageFlags.DefaultKeySet) throw new InvalidOperationException("manual_tls_server_key_flags_invalid");
            // Schannel needs a native key container. No PersistKeySet: certificate disposal removes this temporary key.
            return X509CertificateLoader.LoadPkcs12(pfx, password, flags);
        }
        public void VerifyOwnedDirectory(ManualReviewOwnedDirectory directory)
        {
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "BackendDevelopment", "run20261010b5cmanual");
            var path = Path.GetFullPath(directory.Path);
            if (!path.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("manual_tls_directory_target_invalid");
            AssertNoReparsePoints(path);
            var info = new DirectoryInfo(path); var security = info.GetAccessControl();
            var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("manual_tls_user_unknown");
            if (!security.AreAccessRulesProtected || security.GetOwner(typeof(SecurityIdentifier))?.Equals(sid) != true)
                throw new InvalidDataException("manual_tls_directory_acl_invalid");
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            if (rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(sid))
                || !rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Equals(sid)
                    && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl))
                throw new InvalidDataException("manual_tls_directory_acl_invalid");
            var ownerPath = Path.Combine(path, "owner-receipt.json"); AssertNoReparsePoints(ownerPath);
            using var receipt = JsonDocument.Parse(File.ReadAllBytes(ownerPath));
            var root = receipt.RootElement;
            if (root.GetProperty("Version").GetInt32() != 1 || root.GetProperty("RunName").GetString() != "run20261010b5cmanual"
                || root.GetProperty("OwnerId").GetGuid() != directory.OwnerId
                || !string.Equals(root.GetProperty("Directory").GetString(), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("manual_tls_directory_owner_invalid");
            if (Directory.EnumerateFileSystemEntries(path).Any(entry => !entry.Equals(ownerPath, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("manual_tls_directory_not_new");
        }

        public void WriteNewFile(string path, byte[] bytes)
        {
            AssertNoReparsePoints(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            file.Write(bytes); file.Flush(true);
        }
        public byte[] ProtectForCurrentUser(byte[] bytes, Guid ownerId)
        {
            var entropyBytes = Encoding.UTF8.GetBytes($"Dango/BackendManualReview/v1/{ownerId:D}");
            var input = new DataBlob { Length = bytes.Length };
            var entropy = new DataBlob { Length = entropyBytes.Length };
            DataBlob output = default;
            try
            {
                input.Data = Marshal.AllocHGlobal(bytes.Length); entropy.Data = Marshal.AllocHGlobal(entropyBytes.Length);
                Marshal.Copy(bytes, 0, input.Data, bytes.Length); Marshal.Copy(entropyBytes, 0, entropy.Data, entropyBytes.Length);
                if (!CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output))
                    throw new IOException("manual_tls_dpapi_failed");
                var encrypted = new byte[output.Length]; Marshal.Copy(output.Data, encrypted, 0, encrypted.Length); return encrypted;
            }
            finally
            {
                if (input.Data != IntPtr.Zero)
                {
                    for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                    Marshal.FreeHGlobal(input.Data);
                }
                if (entropy.Data != IntPtr.Zero) Marshal.FreeHGlobal(entropy.Data);
                if (output.Data != IntPtr.Zero) _ = LocalFree(output.Data);
            }
        }
        public IReadOnlyList<X509Certificate2> FindCurrentUserRoot(string thumbprint)
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var all = store.Certificates;
            try { return all.Cast<X509Certificate2>().Where(cert => cert.Thumbprint == thumbprint).Select(cert => X509CertificateLoader.LoadCertificate(cert.RawData)).ToArray(); }
            finally { foreach (var certificate in all) certificate.Dispose(); }
        }
        public void AddCurrentUserRoot(X509Certificate2 publicCertificate)
        {
            if (publicCertificate.HasPrivateKey) throw new InvalidOperationException("manual_tls_private_key_import_forbidden");
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly); store.Add(publicCertificate);
        }
        public void RemoveCurrentUserRoot(X509Certificate2 publicCertificate)
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly); store.Remove(publicCertificate);
        }
        private static void AssertNoReparsePoints(string path)
        {
            for (var cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("manual_tls_reparse_point");
        }
        [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref DataBlob input, string? description,
            ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    }
}
