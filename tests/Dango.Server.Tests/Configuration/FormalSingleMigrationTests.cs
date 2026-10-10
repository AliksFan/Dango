using Dango.Server.Accounts;
using Dango.Server.Hosting;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Configuration;

[TestClass]
public sealed class FormalSingleMigrationTests
{
    private const string Target = "20261010055529_AccountProfile";
    private const string Later = "20990101000000_Unapproved";

    [TestMethod]
    public async Task ApprovedSingleMigrationCannotInvokeTheLatestTarget()
    {
        var migrator = new RecordingMigrator();
        await FormalSingleMigration.ApplyAsync(migrator, Target, [Target, Later], [Target]);
        CollectionAssert.AreEqual(new[] { Target }, migrator.Targets.ToArray());
    }

    [TestMethod]
    public async Task AlreadyAppliedTargetCannotDowngradeLaterHistory()
    {
        var migrator = new RecordingMigrator();
        await FormalSingleMigration.ApplyAsync(migrator, Target, [Target, Later], []);
        Assert.AreEqual(0, migrator.Targets.Count);
    }

    [TestMethod]
    public async Task ChangedPendingSetIsRejectedBeforeAnyMigration()
    {
        foreach (var pending in new string[][] { [Target, Later], [Later] })
        {
            var migrator = new RecordingMigrator();
            var fault = await Assert.ThrowsExactlyAsync<AccountFault>(() =>
                FormalSingleMigration.ApplyAsync(migrator, Target, [Target, Later], pending));
            Assert.AreEqual("maintenance_migration_target_invalid", fault.Code);
            Assert.AreEqual(0, migrator.Targets.Count);
        }
    }

    [TestMethod]
    public async Task UnknownTargetAndMigrationFailureNeverReportSuccess()
    {
        var missing = new RecordingMigrator();
        await Assert.ThrowsExactlyAsync<AccountFault>(() =>
            FormalSingleMigration.ApplyAsync(missing, Target, [Later], [Target]));
        Assert.AreEqual(0, missing.Targets.Count);

        var failed = new RecordingMigrator { Fail = true };
        await Assert.ThrowsExactlyAsync<InjectedMigrationFailure>(() =>
            FormalSingleMigration.ApplyAsync(failed, Target, [Target], [Target]));
        CollectionAssert.AreEqual(new[] { Target }, failed.Targets.ToArray());
    }

    private sealed class InjectedMigrationFailure : Exception;

    private sealed class RecordingMigrator : IMigrator
    {
        public List<string?> Targets { get; } = [];
        public bool Fail { get; init; }
        public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
        {
            Targets.Add(targetMigration);
            return Fail ? Task.FromException(new InjectedMigrationFailure()) : Task.CompletedTask;
        }
        public void Migrate(string? targetMigration = null) => throw new AssertFailedException("Synchronous migration is outside this entry.");
        public string GenerateScript(string? fromMigration = null, string? toMigration = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) => throw new NotSupportedException();
        public bool HasPendingModelChanges() => throw new NotSupportedException();
    }
}
