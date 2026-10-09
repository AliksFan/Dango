using Dango.Server.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.Persistence;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class PostgresFoundationTests
{
    private static string connectionString = string.Empty;
    private static string stateDirectory = string.Empty;
    private const string Configuration = "{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#FFFFFF\",\"eyeColorHex\":\"#000000\",\"eyeSizePercent\":50,\"eyeOvalityPercent\":50,\"eyeHeightControlPercent\":50,\"eyeSpacingControlPercent\":50}";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        var supplied = Environment.GetEnvironmentVariable("DANGO_BACKEND_TEST_CONNECTION");
        var expectedDirectory = Environment.GetEnvironmentVariable("DANGO_BACKEND_TEST_DATA_DIRECTORY");
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expectedDirectory))
            Assert.Inconclusive("An explicitly authorized isolated PostgreSQL target is required.");
        var database = new NpgsqlConnectionStringBuilder(supplied)
        {
            IncludeErrorDetail = false,
            LogParameters = false,
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 15,
            Timezone = "UTC",
        };
        Assert.AreEqual("127.0.0.1", database.Host);
        Assert.AreEqual("dango_backend_b0", database.Database);
        connectionString = database.ConnectionString;
        stateDirectory = Path.GetDirectoryName(Path.GetFullPath(expectedDirectory!))!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_database(), current_setting('server_version_num')::integer, current_setting('data_directory')", connection);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual("dango_backend_b0", reader.GetString(0));
            Assert.IsTrue(reader.GetInt32(1) is >= 180000 and < 190000);
            Assert.AreEqual(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedDirectory!)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(reader.GetString(2))), true);
        }
        // Only after verifying database, version and physical cluster may this opt-in gate write.
        await using var context = new DangoDbContext(DangoDatabaseOptions.Create(connectionString));
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        foreach (var migration in pending)
        {
            if (migration == "20261008074701_InitialAccountRoomFoundation") continue;
            if (migration == "20261008085521_AccountLifecycle"
                && Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_TEST_APPROVED") == "1") continue;
            if (migration == "20261009031410_RoomManagement"
                && Environment.GetEnvironmentVariable("DANGO_BACKEND_B3_TEST_APPROVED") == "1") continue;
            Assert.Inconclusive("Each additional migration requires its own precise stage authorization.");
        }
        await context.Database.MigrateAsync();
    }

    [TestMethod]
    public async Task MigrationReapplicationPreservesTheVersionAndSchema()
    {
        await using var context = new DangoDbContext(DangoDatabaseOptions.Create(connectionString));
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        CollectionAssert.AreEqual(context.Database.GetMigrations().ToArray(), before);
        Assert.Contains("20261008074701_InitialAccountRoomFoundation", before);
        await context.Database.MigrateAsync();
        CollectionAssert.AreEqual(before, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.IsFalse(context.Database.HasPendingModelChanges());
    }

    [TestMethod]
    public async Task NormalizedEmailPetOwnerAndDefaultRoomAreUniqueInPostgres()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Reject("INSERT INTO dango.users (id, created_at, email, normalized_email, email_confirmed, password_hash, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) VALUES (@id, @now, @email, @normalized, true, 'not_a_password_hash', false, false, true, 0)",
            "23505", "ux_users_normalized_email", ("id", Guid.NewGuid()), ("now", DateTimeOffset.UtcNow),
            ("email", fixture.Email.ToLowerInvariant()), ("normalized", fixture.Email.ToUpperInvariant()));
        await fixture.Reject("INSERT INTO dango.pets (id, user_id, configuration, configuration_version, created_at) VALUES (@id, @owner, @configuration::jsonb, 1, @now)",
            "23505", "ux_pets_user_id", ("id", Guid.NewGuid()), ("owner", fixture.UserId), ("configuration", Configuration), ("now", DateTimeOffset.UtcNow));
        await fixture.Reject("INSERT INTO dango.rooms (id, owner_id, kind, status, name, capacity, version, created_at, template_id) VALUES (@id, @owner, 0, 0, 'fictional room', 2, 1, @now, 'desktop-pet-default')",
            "23505", "ux_rooms_desktop_owner", ("id", Guid.NewGuid()), ("owner", fixture.UserId), ("now", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task MissingParentsAndCascadingDeletionAreRejected()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Reject("UPDATE dango.pets SET user_id = @missing WHERE id = @pet", "23503", "FK_pets_users_user_id",
            ("missing", Guid.NewGuid()), ("pet", fixture.PetId));
        await fixture.Reject("DELETE FROM dango.users WHERE id = @user", PostgresErrorCodes.RestrictViolation, null, ("user", fixture.UserId));
    }

    [TestMethod]
    public async Task CapacityLifecycleAndOwnerMembershipConstraintsExecute()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Reject("UPDATE dango.rooms SET capacity = 1 WHERE id = @room", "23514", "ck_rooms_capacity", ("room", fixture.RoomId));
        foreach (var valid in new[] { 1, 12 })
            await fixture.Execute("INSERT INTO dango.rooms (id, owner_id, kind, status, name, capacity, version, created_at) VALUES (@id, @owner, 2, 0, 'fictional room', @capacity, 1, @now)",
                ("id", Guid.NewGuid()), ("owner", fixture.UserId), ("capacity", valid), ("now", DateTimeOffset.UtcNow));
        foreach (var invalid in new[] { 0, 13 })
            await fixture.Reject("INSERT INTO dango.rooms (id, owner_id, kind, status, name, capacity, version, created_at) VALUES (@id, @owner, 2, 0, 'fictional room', @capacity, 1, @now)",
                "23514", "ck_rooms_capacity", ("id", Guid.NewGuid()), ("owner", fixture.UserId), ("capacity", invalid), ("now", DateTimeOffset.UtcNow));
        await fixture.Reject("UPDATE dango.rooms SET status = 1 WHERE id = @room", "23514", "ck_rooms_lifecycle", ("room", fixture.RoomId));
        await fixture.Reject("UPDATE dango.room_memberships SET role = 9 WHERE room_id = @room", "23514", "ck_memberships_role", ("room", fixture.RoomId));
    }

    [TestMethod]
    public async Task NoRoomClearsPresenceAndNonFiniteCoordinatesAreRejected()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Reject("UPDATE dango.pet_room_states SET room_id = NULL WHERE pet_id = @pet", "23514", "ck_pet_room_presence", ("pet", fixture.PetId));
        foreach (var invalid in new[] { "NaN", "Infinity", "-Infinity" })
            await fixture.Reject("UPDATE dango.pet_room_states SET position_x = @position::double precision WHERE pet_id = @pet", "23514", "ck_pet_room_presence",
                ("position", invalid), ("pet", fixture.PetId));
        await fixture.Execute("UPDATE dango.pet_room_states SET room_id = NULL, position_x = NULL, position_y = NULL, is_sleeping = false WHERE pet_id = @pet", ("pet", fixture.PetId));
        await fixture.Reject("UPDATE dango.pet_room_states SET is_sleeping = true WHERE pet_id = @pet", "23514", "ck_pet_room_presence", ("pet", fixture.PetId));
    }

    [TestMethod]
    public async Task InvalidAppearanceSchemaAndUnverifiedUsersAreRejected()
    {
        await using var fixture = await Fixture.Create();
        foreach (var invalid in new[] { "[]", "{}", "{\"appearanceSchemaVersion\":4}", "{\"appearanceSchemaVersion\":null}" })
            await fixture.Reject("UPDATE dango.pets SET configuration = @configuration::jsonb WHERE id = @pet", "23514", "ck_pets_configuration",
                ("configuration", invalid), ("pet", fixture.PetId));
        await fixture.Reject("UPDATE dango.users SET email_confirmed = false WHERE id = @user", "23514", "ck_users_verified", ("user", fixture.UserId));
    }

    [TestMethod]
    public async Task RecoveryDeadlineIsExactly168HoursAcrossDaylightSavingTransition()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Execute("SET LOCAL TIME ZONE 'America/New_York'");
        var dissolved = new DateTimeOffset(2026, 3, 6, 12, 0, 0, TimeSpan.Zero);
        var roomId = Guid.NewGuid();
        await fixture.Execute("INSERT INTO dango.rooms (id, owner_id, kind, status, name, capacity, version, created_at, dissolved_at, recover_until) VALUES (@id, @owner, 1, 1, 'fictional recovery room', 6, 1, @start, @start, @end)",
            ("id", roomId), ("owner", fixture.UserId), ("start", dissolved), ("end", dissolved.AddHours(168)));
        await fixture.Reject("UPDATE dango.rooms SET recover_until = @end WHERE id = @room", "23514", "ck_rooms_lifecycle",
            ("end", dissolved.AddHours(167)), ("room", roomId));
    }

    [TestMethod]
    public async Task CommittedFoundationIsReadableFromIndependentConnectionAndSubsequentRun()
    {
        var probePath = Path.Combine(stateDirectory, "foundation-probe.json");
        FoundationProbe probe;
        if (File.Exists(probePath))
        {
            probe = JsonSerializer.Deserialize<FoundationProbe>(await File.ReadAllTextAsync(probePath))!;
            Assert.IsNotNull(probe);
        }
        else
        {
            await using var fixture = await Fixture.Create("foundation-probe@example.invalid");
            await fixture.Commit();
            probe = new(fixture.UserId, fixture.PetId, fixture.RoomId);
            await File.WriteAllTextAsync(probePath, JsonSerializer.Serialize(probe));
        }
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM dango.users u JOIN dango.pets p ON p.user_id=u.id JOIN dango.rooms r ON r.owner_id=u.id AND r.kind=0 JOIN dango.room_memberships m ON m.room_id=r.id AND m.user_id=u.id AND m.role=0 JOIN dango.pet_room_states s ON s.pet_id=p.id AND s.room_id=r.id WHERE u.id=@user AND p.id=@pet AND r.id=@room AND p.configuration_version=1 AND p.configuration->'appearanceSchemaVersion'='3'::jsonb", connection);
        command.Parameters.AddWithValue("user", probe.UserId);
        command.Parameters.AddWithValue("pet", probe.PetId);
        command.Parameters.AddWithValue("room", probe.RoomId);
        Assert.AreEqual(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    private sealed record FoundationProbe(Guid UserId, Guid PetId, Guid RoomId);

    private sealed class Fixture(NpgsqlConnection connection, NpgsqlTransaction transaction, string? fixedEmail) : IAsyncDisposable
    {
        private bool committed;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PetId { get; } = Guid.NewGuid();
        public Guid RoomId { get; } = Guid.NewGuid();
        public string Email => fixedEmail ?? $"fictional-{UserId:N}@example.invalid";

        public static async Task<Fixture> Create(string? fixedEmail = null)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var fixture = new Fixture(connection, await connection.BeginTransactionAsync(), fixedEmail);
            try
            {
                await fixture.Execute("INSERT INTO dango.users (id, created_at, email, normalized_email, email_confirmed, password_hash, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) VALUES (@id, @now, @email, @normalized, true, 'not_a_password_hash', false, false, true, 0)",
                    ("id", fixture.UserId), ("now", DateTimeOffset.UtcNow), ("email", fixture.Email), ("normalized", fixture.Email.ToUpperInvariant()));
                await fixture.Execute("INSERT INTO dango.pets (id, user_id, configuration, configuration_version, created_at) VALUES (@id, @owner, @configuration::jsonb, 1, @now)",
                    ("id", fixture.PetId), ("owner", fixture.UserId), ("configuration", Configuration), ("now", DateTimeOffset.UtcNow));
                await fixture.Execute("INSERT INTO dango.rooms (id, owner_id, kind, status, name, capacity, version, created_at, template_id) VALUES (@id, @owner, 0, 0, 'fictional desktop room', 2, 1, @now, 'desktop-pet-default')",
                    ("id", fixture.RoomId), ("owner", fixture.UserId), ("now", DateTimeOffset.UtcNow));
                await fixture.Execute("INSERT INTO dango.room_memberships (room_id, user_id, role) VALUES (@room, @owner, 0)", ("room", fixture.RoomId), ("owner", fixture.UserId));
                await fixture.Execute("INSERT INTO dango.pet_room_states (pet_id, room_id, position_x, position_y, is_sleeping, version, control_generation) VALUES (@pet, @room, 0.5, 0.5, false, 1, 0)",
                    ("pet", fixture.PetId), ("room", fixture.RoomId));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task Execute(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task Reject(string sql, string state, string? constraint, params (string Name, object Value)[] parameters)
        {
            await transaction.SaveAsync("constraint_probe");
            try
            {
                try { await Execute(sql, parameters); Assert.Fail("The invalid database write was accepted."); }
                catch (PostgresException exception)
                {
                    Assert.AreEqual(state, exception.SqlState);
                    if (constraint is not null) Assert.AreEqual(constraint, exception.ConstraintName);
                }
            }
            finally { await transaction.RollbackAsync("constraint_probe"); }
        }

        public async ValueTask DisposeAsync()
        {
            if (!committed) await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }

        public async Task Commit()
        {
            await transaction.CommitAsync();
            committed = true;
        }
    }
}
