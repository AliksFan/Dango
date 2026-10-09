using Dango.Server.Accounts;
using Dango.Server.Configuration;

namespace Dango.Server.Tests.Configuration;

[TestClass]
public sealed class RoomMaintenanceOptionsTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("false")]
    public void PermanentExpiryIsDisabledWithoutExplicitActivation(string? value) => Assert.IsFalse(RoomMaintenanceOptions.ExpiryEnabled(value));

    [TestMethod]
    public void ExplicitTrueActivatesTheConfiguredDatabaseWorker() => Assert.IsTrue(RoomMaintenanceOptions.ExpiryEnabled("true"));

    [TestMethod]
    [DataRow("1")]
    [DataRow("TRUE")]
    [DataRow(" true ")]
    [DataRow("yes")]
    public void MalformedActivationFailsInsteadOfStartingCleanup(string value) =>
        Assert.AreEqual("room_expiry_configuration_invalid", Assert.ThrowsExactly<AccountFault>(() => RoomMaintenanceOptions.ExpiryEnabled(value)).Code);
}
