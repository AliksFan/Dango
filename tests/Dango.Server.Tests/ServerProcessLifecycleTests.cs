using System.Diagnostics;
using System.Text.RegularExpressions;
using Dango.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests;

[TestClass]
public sealed class ServerProcessLifecycleTests
{
    [TestMethod]
    public async Task StartupFailureAfterPrivateBootstrapReapsTheAlreadyRunningChild()
    {
        var binary = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(binary);
        int? pid = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var child = await LocalServerProcess.StartAsync(start, binary, message =>
            {
                pid = int.Parse(Regex.Match(message, @"PID=(\d+)").Groups[1].Value);
                throw new InvalidOperationException("synthetic startup log failure");
            });
        });
        Assert.IsNotNull(pid, "Failure must occur after this fixture has bootstrapped its own child.");
        try
        {
            using var child = Process.GetProcessById(pid.Value);
            Assert.IsTrue(child.HasExited, "Failed startup left its local server running.");
        }
        catch (ArgumentException) { /* Windows already reaped the exited process. */ }
    }
}
