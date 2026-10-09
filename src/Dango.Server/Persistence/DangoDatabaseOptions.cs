using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Persistence;

public static class DangoDatabaseOptions
{
    // Shared by runtime and tooling. Omitting a connection is safe for metadata and migration generation.
    public static DbContextOptions<DangoDbContext> Create(string? connectionString = null)
    {
        var builder = new DbContextOptionsBuilder<DangoDbContext>();
        builder.UseNpgsql(connectionString, postgres =>
        {
            postgres.SetPostgresVersion(18, 0);
            postgres.MigrationsHistoryTable("__ef_migrations_history", "dango");
            postgres.CommandTimeout(15);
        });
        builder.EnableSensitiveDataLogging(false);
        builder.EnableDetailedErrors(false);
        return builder.Options;
    }
}
