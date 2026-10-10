using Dango.Server.Accounts;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dango.Server.Hosting;

internal static class FormalSingleMigration
{
    public static async Task ApplyAsync(IMigrator migrator, string expectedMigration,
        IEnumerable<string> availableMigrations, IReadOnlyList<string> pendingMigrations,
        CancellationToken ct = default)
    {
        if (!availableMigrations.Contains(expectedMigration, StringComparer.Ordinal)
            || pendingMigrations.Count > 1
            || (pendingMigrations.Count == 1 && pendingMigrations[0] != expectedMigration))
            throw new AccountFault("maintenance_migration_target_invalid");

        // No pending migration means no work. Calling an earlier target would downgrade
        // a database which already includes later migrations.
        if (pendingMigrations.Count == 0) return;
        await migrator.MigrateAsync(expectedMigration, ct);
    }
}
