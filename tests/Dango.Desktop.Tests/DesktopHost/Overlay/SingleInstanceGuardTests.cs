using Dango.Desktop.DesktopHost.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Overlay;

[TestClass]
public sealed class SingleInstanceGuardTests
{
    [TestMethod]
    public void ASecondHostCannotAcquireTheSameProcessGuard()
    {
        var name = $"Local\\Dango.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.TryAcquire(name);
        using var second = SingleInstanceGuard.TryAcquire(name);

        Assert.IsNotNull(first);
        Assert.IsNull(second);
    }

    [TestMethod]
    public void GuardCanBeAcquiredAgainAfterTheHostExits()
    {
        var name = $"Local\\Dango.Tests.{Guid.NewGuid():N}";
        var first = SingleInstanceGuard.TryAcquire(name);
        Assert.IsNotNull(first);
        first.Dispose();

        using var restarted = SingleInstanceGuard.TryAcquire(name);

        Assert.IsNotNull(restarted);
    }
}
