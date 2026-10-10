using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.BackendManualReview.Tests;

[TestClass]
public sealed class ManualReviewCredentialsTests
{
    private static readonly Uri Origin = new("https://127.0.0.1:54321");
    [TestMethod] public void TargetsMatchProductCanonicalHashAndSlots()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("https://127.0.0.1:54321")));
        Assert.AreEqual($"Dango/EmailBackend/v1/{hash}/session", ManualReviewCredentials.Target(Origin, "session"));
        Assert.AreEqual($"Dango/EmailBackend/v1/{hash}/registration", ManualReviewCredentials.Target(Origin, "registration"));
    }
    [TestMethod] public void InvalidOriginsAndSlotsFailBeforeExistenceRead()
    {
        var fake = new Credentials(); var service = new ManualReviewCredentials(fake);
        foreach (var origin in new[] { "http://127.0.0.1:54321", "https://user@127.0.0.1:54321", "https://127.0.0.1:54321/extra", "https://127.0.0.1:54321/?x=1", "https://localhost:54321" })
            Assert.ThrowsExactly<InvalidDataException>(() => service.ReserveUnusedOrigin(true, new Uri(origin)));
        Assert.ThrowsExactly<InvalidDataException>(() => ManualReviewCredentials.Target(Origin, "other"));
        Assert.AreEqual(0, fake.Reads.Count);
    }
    [TestMethod] public void AuthorizationRejectsBeforeReadOrDeletion()
    {
        var fake = new Credentials(); var service = new ManualReviewCredentials(fake);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.ReserveUnusedOrigin(false, Origin));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.ClearOnlyOwned(false, new(Origin, "a", "b")));
        Assert.AreEqual(0, fake.Reads.Count + fake.Deletes.Count);
    }
    [TestMethod] public void ExistingRegistrationNeverAuthorizesCleanup()
    {
        var fake = new Credentials(); var service = new ManualReviewCredentials(fake);
        fake.Existing.Add(ManualReviewCredentials.Target(Origin, "registration"));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.ReserveUnusedOrigin(true, Origin));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.ClearOnlyOwned(true, new(Origin, ManualReviewCredentials.Target(Origin, "session"), ManualReviewCredentials.Target(Origin, "registration"))));
        Assert.AreEqual(0, fake.Deletes.Count);
    }
    [TestMethod] public void BothAbsentIsOnlyDeletionAuthorityAndClearsExactTwoSlots()
    {
        var fake = new Credentials(); var service = new ManualReviewCredentials(fake);
        var slots = service.ReserveUnusedOrigin(true, Origin);
        CollectionAssert.AreEquivalent(new[] { slots.SessionTarget, slots.RegistrationTarget }, fake.Reads);
        service.ClearOnlyOwned(true, slots);
        CollectionAssert.AreEqual(new[] { slots.SessionTarget, slots.RegistrationTarget }, fake.Deletes);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.ClearOnlyOwned(true, slots));
    }
    [TestMethod] public void ForgedCopiedReceiptDoesNotGrantOwnership()
    {
        var fake = new Credentials(); var service = new ManualReviewCredentials(fake);
        var slots = service.ReserveUnusedOrigin(true, Origin);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.ClearOnlyOwned(true, slots with { }));
        Assert.AreEqual(0, fake.Deletes.Count);
    }
    [TestMethod] public void FailedExistenceReadCannotAuthorizeDeletion()
    {
        var fake = new Credentials { FailReadNumber = 2 }; var service = new ManualReviewCredentials(fake);
        Assert.ThrowsExactly<IOException>(() => service.ReserveUnusedOrigin(true, Origin));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.ClearOnlyOwned(true, new(Origin, "a", "b")));
        Assert.AreEqual(0, fake.Deletes.Count);
    }
    private sealed class Credentials : IManualReviewCredentialResources
    {
        public List<string> Reads { get; } = []; public List<string> Deletes { get; } = [];
        public HashSet<string> Existing { get; } = []; public int FailReadNumber { get; set; }
        public bool Exists(string exactTarget) { Reads.Add(exactTarget); if (Reads.Count == FailReadNumber) throw new IOException(); return Existing.Contains(exactTarget); }
        public void Delete(string exactTarget) => Deletes.Add(exactTarget);
    }
}
