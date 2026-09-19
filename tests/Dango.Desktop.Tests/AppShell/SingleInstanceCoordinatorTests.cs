using Dango.Desktop.AppShell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class SingleInstanceCoordinatorTests
{
    [TestMethod]
    public void ASecondLaunchCanSignalThePrimaryInstance()
    {
        var name = $"Local\\Dango.Tests.{Guid.NewGuid():N}";
        using var activated = new ManualResetEventSlim();
        using var primary = SingleInstanceCoordinator.TryBecomePrimary(name, activated.Set);

        Assert.IsNotNull(primary);
        using var second = SingleInstanceCoordinator.TryBecomePrimary(name, () => { });
        Assert.IsNull(second);
        Assert.IsTrue(SingleInstanceCoordinator.TrySignalPrimary(name));
        Assert.IsTrue(activated.Wait(TimeSpan.FromSeconds(3)), "主实例未收到激活请求。");
    }
}
