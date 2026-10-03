using System.IO;
using Dango.MultiplayerSample;

namespace Dango.Desktop.Tests.Multiplayer;

[TestClass]
public sealed class SessionIsolationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RejectsTraversalAndDailyDirectoriesBeforeCreatingAnything()
    {
        var root = Path.Combine(TestContext.TestRunDirectory!, "session-isolation-" + Guid.NewGuid().ToString("N"));
        foreach (var name in new[] { "../daily", "..", "A/B", "A\\B", "C:\\daily", "", ".settings" })
            Assert.ThrowsExactly<ArgumentException>(() => SessionFiles.Create(root, name));
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void ExistingSessionIsNeverReusedAndClientSettingsAreSeparate()
    {
        var root = Path.Combine(TestContext.TestRunDirectory!, "session-isolation-" + Guid.NewGuid().ToString("N"));
        var session = SessionFiles.Create(root, "fictional-run");
        var a = SessionFiles.ResolveClient(session, "A");
        var b = SessionFiles.ResolveClient(session, "B");
        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a.StartsWith(root, StringComparison.Ordinal));
        Assert.ThrowsExactly<InvalidOperationException>(() => SessionFiles.Create(root, "fictional-run"));
        Assert.ThrowsExactly<ArgumentException>(() => SessionFiles.ResolveClient(session, "../daily"));
        Assert.IsTrue(Directory.Exists(a) && Directory.Exists(b));
    }
}
