using Dango.Desktop.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Verification;

[TestClass]
public sealed class WorkerWVerificationDefinitionTests
{
    [TestMethod]
    public void P1003DefinitionCoversShellRecoveryAndDesktopBehavior()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var ids = definition.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        Assert.AreEqual("p1-003-workerw", definition.Id);
        Assert.IsTrue(ids.Contains("workerw-attached"));
        Assert.IsTrue(ids.Contains("explorer-restart"));
        Assert.IsTrue(ids.Contains("safe-recovery-failure"));
        Assert.IsTrue(ids.Contains("desktop-icons"));
        Assert.IsTrue(ids.Contains("repeat-lifecycle"));
        Assert.IsTrue(ids.Contains("dpi-high"));
        Assert.IsTrue(definition.Items.All(item => item.IsRequired));
    }
}
