using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.BackendManualReview.Tests;

[TestClass]
public sealed class ManualReviewTlsTests
{
    private static ManualReviewOwnedDirectory Directory => new(@"C:\fictional-protected-root", Guid.NewGuid(), true, true);
    [TestMethod] public void AuthorizationRefusesBeforeFileDpapiOrRootStore()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Prepare(false,Directory,DateTimeOffset.UtcNow));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Trust(false,null!));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.RemoveOwnedTrust(false,null!));
        Assert.AreEqual(0, fake.Operations);
    }
    [TestMethod] public void IncompleteOwnedDirectoryProofCannotGenerateFiles()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);
        Assert.ThrowsExactly<InvalidDataException>(() => service.Prepare(true,Directory with { CreatedForThisRun = false },DateTimeOffset.UtcNow));
        Assert.ThrowsExactly<InvalidDataException>(() => service.Prepare(true,Directory with { AclProtectedForCurrentUser = false },DateTimeOffset.UtcNow));
        Assert.AreEqual(0, fake.Operations);
    }
    [TestMethod] public void PreparedCertificateHasOnlyApprovedTlsIdentityAndProtectedPassword()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);
        using var material = service.Prepare(true,Directory,DateTimeOffset.UtcNow);
        ManualReviewTls.ValidateCertificate(material.Certificate);
        Assert.IsTrue(material.Certificate.HasPrivateKey);
        Assert.IsTrue(material.Receipt.NotAfter-material.Receipt.NotBefore <= TimeSpan.FromDays(3));
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(material.Certificate.RawData)),material.Receipt.RawSha256);
        Assert.AreEqual(4,fake.Files.Count);
        Assert.AreEqual(1,fake.ProtectedCount);
        CollectionAssert.AreEqual(new byte[] { 1,2,3 }, fake.Files.Single(p=>p.Key.EndsWith("tls-password.dpapi",StringComparison.Ordinal)).Value);
        Assert.AreEqual(0,fake.Root.Count);
    }
    [TestMethod] public void TrustImportsOnlyPublicCertificateAndExactCleanupPreservesFiles()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);
        using var material = service.Prepare(true,Directory,DateTimeOffset.UtcNow);
        service.Trust(true,material);
        Assert.IsFalse(fake.Root.Single().HasPrivateKey);
        service.RemoveOwnedTrust(true,material.Receipt);
        Assert.AreEqual(0,fake.Root.Count); Assert.AreEqual(4,fake.Files.Count); Assert.AreEqual(1,fake.Removes);
    }
    [TestMethod] public void ExistingSameThumbprintRefusesTrustAndCannotRemoveIt()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);
        using var material = service.Prepare(true,Directory,DateTimeOffset.UtcNow);
        fake.Root.Add(X509CertificateLoader.LoadCertificate(material.Certificate.RawData));
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.Trust(true,material));
        Assert.IsFalse(service.CanRemoveOwnedTrust(material.Receipt));
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.RemoveOwnedTrust(true,material.Receipt));
        Assert.AreEqual(0,fake.Removes);
    }
    [TestMethod] public void CopiedOrModifiedReceiptCannotRemoveTrust()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);
        using var material = service.Prepare(true,Directory,DateTimeOffset.UtcNow); service.Trust(true,material);
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.RemoveOwnedTrust(true,material.Receipt with {}));
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.RemoveOwnedTrust(true,material.Receipt with { RawSha256 = new('0',64) }));
        Assert.AreEqual(0,fake.Removes);
    }
    [TestMethod] public void RootReplacementOrDuplicateRefusesCleanup()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);
        using var material = service.Prepare(true,Directory,DateTimeOffset.UtcNow); service.Trust(true,material);
        fake.Root.Add(X509CertificateLoader.LoadCertificate(material.Certificate.RawData));
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.RemoveOwnedTrust(true,material.Receipt));
        Assert.AreEqual(0,fake.Removes);
    }
    [TestMethod] public void CaWrongSanExtraEkuAndLongValidityAreRejected()
    {
        foreach (var variant in new[] { "ca","dns","extraIp","extraEku","long" })
        {
            using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=fictional",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(variant=="ca",false,0,true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            var san = new SubjectAlternativeNameBuilder(); if(variant=="dns") san.AddDnsName("localhost"); else san.AddIpAddress(System.Net.IPAddress.Loopback);
            if(variant=="extraIp") san.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(san.Build());
            var purposes = new OidCollection {new("1.3.6.1.5.5.7.3.1")}; if(variant=="extraEku") purposes.Add(new("1.3.6.1.5.5.7.3.2"));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes,true));
            using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddDays(variant=="long"?4:1));
            Assert.ThrowsExactly<InvalidDataException>(()=>ManualReviewTls.ValidateCertificate(cert));
        }
    }
    [TestMethod] public void ExistingFileFailureDoesNotImportTrust()
    {
        var fake = new Resources { FailWrite = true }; var service = new ManualReviewTls(fake);
        Assert.ThrowsExactly<IOException>(()=>service.Prepare(true,Directory,DateTimeOffset.UtcNow));
        Assert.AreEqual(0,fake.Root.Count);
        Assert.IsNotNull(fake.LoadedCertificate);
        Assert.AreEqual(IntPtr.Zero,fake.LoadedCertificate.Handle);
    }
    [TestMethod] public void AmbiguousImportFailureStillAllowsOnlyThisNewPublicTrustCleanup()
    {
        var fake = new Resources { FailAfterAdd = true }; var service = new ManualReviewTls(fake);
        using var material=service.Prepare(true,Directory,DateTimeOffset.UtcNow);
        Assert.ThrowsExactly<IOException>(()=>service.Trust(true,material));
        Assert.IsTrue(service.CanRemoveOwnedTrust(material.Receipt));
        Assert.AreEqual(1,fake.Root.Count);
        service.RemoveOwnedTrust(true,material.Receipt);
        Assert.AreEqual(0,fake.Root.Count); Assert.AreEqual(1,fake.Removes);
    }
    [TestMethod] public void PrepareTwiceForSameOwnedRunRefusesBeforeAnotherWrite()
    {
        var fake = new Resources(); var service = new ManualReviewTls(fake);var directory=Directory;
        using var material=service.Prepare(true,directory,DateTimeOffset.UtcNow);
        var operations=fake.Operations;
        Assert.ThrowsExactly<InvalidOperationException>(()=>service.Prepare(true,directory,DateTimeOffset.UtcNow));
        Assert.AreEqual(operations,fake.Operations);
    }
    [TestMethod] public void PreparedServerCertificateReloadsSameEncryptedPfxWithTemporaryNativeKeyPolicy()
    {
        var fake=new Resources();var service=new ManualReviewTls(fake);
        using var material=service.Prepare(true,Directory,DateTimeOffset.UtcNow);
        Assert.AreEqual(1,fake.Loads);
        Assert.AreEqual(X509KeyStorageFlags.DefaultKeySet,fake.LastStorageFlags);
        Assert.AreSame(fake.LoadedCertificate,material.Certificate);
        Assert.IsTrue(material.Certificate.HasPrivateKey);
        CollectionAssert.AreEqual(fake.LoadedPfx!,fake.Files.Single(file=>file.Key.EndsWith("tls-server.pfx",StringComparison.Ordinal)).Value);
    }
    [TestMethod] public void MaterialDisposalOwnsTheReloadedServerCertificateLifetime()
    {
        var fake=new Resources();var service=new ManualReviewTls(fake);
        var material=service.Prepare(true,Directory,DateTimeOffset.UtcNow);
        Assert.AreNotEqual(IntPtr.Zero,fake.LoadedCertificate!.Handle);
        material.Dispose();
        Assert.AreEqual(IntPtr.Zero,fake.LoadedCertificate.Handle);
    }
    private sealed class Resources : IManualReviewTlsResources
    {
        public int Operations {get;private set;} public int ProtectedCount {get;private set;} public int Removes {get;private set;} public bool FailWrite {get;init;} public bool FailAfterAdd {get;init;}
        public Dictionary<string,byte[]> Files {get;}=[]; public List<X509Certificate2> Root {get;}=[];
        public int Loads {get;private set;} public X509KeyStorageFlags LastStorageFlags {get;private set;} public X509Certificate2? LoadedCertificate {get;private set;} public byte[]? LoadedPfx {get;private set;}
        public X509Certificate2 LoadServerCertificate(byte[] pfx,string password,X509KeyStorageFlags flags)
        {
            Operations++;Loads++;LastStorageFlags=flags;LoadedPfx=pfx.ToArray();
            // Static tests retain keys only in memory rather than creating an actual native container.
            return LoadedCertificate=X509CertificateLoader.LoadPkcs12(pfx,password,X509KeyStorageFlags.EphemeralKeySet);
        }
        public void VerifyOwnedDirectory(ManualReviewOwnedDirectory directory)=>Operations++;
        public byte[] ProtectForCurrentUser(byte[] bytes,Guid ownerId) { Operations++;ProtectedCount++;return [1,2,3]; }
        public void WriteNewFile(string path,byte[] bytes) {Operations++;if(FailWrite)throw new IOException();Files.Add(path,bytes.ToArray());}
        public IReadOnlyList<X509Certificate2> FindCurrentUserRoot(string thumbprint) { Operations++;return Root.Select(c=>X509CertificateLoader.LoadCertificate(c.RawData)).ToArray(); }
        public void AddCurrentUserRoot(X509Certificate2 publicCertificate) {Operations++;Root.Add(X509CertificateLoader.LoadCertificate(publicCertificate.RawData));if(FailAfterAdd)throw new IOException();}
        public void RemoveCurrentUserRoot(X509Certificate2 publicCertificate) {Operations++;Removes++;Root.RemoveAll(c=>c.Thumbprint==publicCertificate.Thumbprint);}
    }
}
