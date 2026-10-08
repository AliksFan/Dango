using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Pets;
using Dango.Server.Tests.Accounts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Pets;

[TestClass]
public sealed class PetConfigurationPolicyTests
{
    [TestMethod]
    public void PersistedConfigurationIsStrictAndReturnedElementSurvivesDocumentDisposal()
    {
        var configuration = PetConfigurationPolicy.ReadPersisted(B1InputTests.ValidAppearance, 1);
        Assert.IsTrue(AppearanceContract.Equivalent(configuration, B1InputTests.Parse(B1InputTests.ValidAppearance)));
        Assert.AreEqual(3, configuration.GetProperty("appearanceSchemaVersion").GetInt32());
    }

    [TestMethod]
    [DataRow("{\"appearanceSchemaVersion\":3}")]
    [DataRow("{broken")]
    [DataRow("[]")]
    public void CorruptPersistedConfigurationIsAnErrorInsteadOfDefaultAppearance(string configuration)
    {
        var fault = Assert.ThrowsExactly<AccountFault>(() => PetConfigurationPolicy.ReadPersisted(configuration, 1));
        Assert.AreEqual("pet_configuration_invalid_state", fault.Code);
        Assert.AreEqual(503, fault.Status);
    }

    [TestMethod]
    public void InvalidPersistedVersionAndOversizedConfigurationFailClosed()
    {
        Assert.ThrowsExactly<AccountFault>(() => PetConfigurationPolicy.ReadPersisted(B1InputTests.ValidAppearance, 0));
        Assert.ThrowsExactly<AccountFault>(() => PetConfigurationPolicy.ReadPersisted(new string(' ', 4097), 1));
    }

    [TestMethod]
    public async Task InvalidUpdateInputFailsBeforeOpeningAnUnconfiguredDatabase()
    {
        await using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var service = new PetConfigurationService(db, new(Guid.NewGuid()), TimeProvider.System, new PetConfigurationCheckpoint());
        var badVersion = await Assert.ThrowsExactlyAsync<AccountFault>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new(0, B1InputTests.Parse(B1InputTests.ValidAppearance)), CancellationToken.None));
        Assert.AreEqual("pet_configuration_version_invalid", badVersion.Code);
        var badAppearance = await Assert.ThrowsExactlyAsync<AccountFault>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new(1, B1InputTests.Parse("{\"appearanceSchemaVersion\":3}")), CancellationToken.None));
        Assert.AreEqual("appearance_invalid", badAppearance.Code);
    }
}
