using Microsoft.EntityFrameworkCore.Design;

namespace Dango.Server.Persistence;

public sealed class DangoDbContextFactory : IDesignTimeDbContextFactory<DangoDbContext>
{
    // No application startup, secret lookup, database connection, migration or seed write here.
    public DangoDbContext CreateDbContext(string[] args) => new(DangoDatabaseOptions.Create());
}
