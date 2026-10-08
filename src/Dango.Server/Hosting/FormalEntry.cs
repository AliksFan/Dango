using System.Security.Cryptography.X509Certificates;
using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Email;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Hosting;

public static class FormalEntry
{
    public static async Task<int> RunAsync(string mode)
    {
        try
        {
            if (!FormalServerOptions.TryCreate(Read("DATABASE"), Read("CONNECTION_STRING"), Read("KEYS_DIRECTORY"),
                Read("ENDPOINT"), FormalDeploymentPaths.ProtectedRoot(), out var options, out var code))
            { Console.Error.WriteLine(code); return 2; }
            if (!Guid.TryParse(Read("RECOVERY_EPOCH"), out var epoch) || epoch == Guid.Empty)
            { Console.Error.WriteLine("recovery_epoch_required"); return 2; }
            var environment = new AccountEnvironment(epoch);
            if (mode == "--formal-migrate")
            {
                // Explicit operator command only. Normal service startup never creates or migrates a database.
                await using var db = new DangoDbContext(DangoDatabaseOptions.Create(options!.ConnectionString));
                if (Read("EXPECTED_DATA_DIRECTORY") is { Length: > 0 } expectedDirectory)
                {
                    await db.Database.OpenConnectionAsync();
                    await using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "SELECT current_database(), current_setting('data_directory'), current_setting('server_version_num')::integer";
                    await using var reader = await command.ExecuteReaderAsync();
                    if (!await reader.ReadAsync() || reader.GetString(0) != options.DatabaseName
                        || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(reader.GetString(1))),
                            Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedDirectory)), StringComparison.OrdinalIgnoreCase)
                        || reader.GetInt32(2) is < 180000 or >= 190000)
                        throw new AccountFault("maintenance_database_target_invalid");
                }
                await db.Database.MigrateAsync();
                Console.WriteLine("formal_migration_applied");
                return 0;
            }
            if (mode is "--formal-invite" or "--formal-invite-revoke")
            {
                await using var db = new DangoDbContext(DangoDatabaseOptions.Create(options!.ConnectionString));
                if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new AccountFault("database_migration_required", 503);
                var invitations = new InvitationService(db, environment, TimeProvider.System);
                if (mode == "--formal-invite")
                {
                    var id = await invitations.IssueAsync(Read("INVITATION_EMAIL") ?? string.Empty,
                        Read("INVITATION_PROOF") ?? string.Empty, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);
                    Console.WriteLine(id.ToString("D"));
                }
                else
                {
                    if (!Guid.TryParse(Read("INVITATION_ID"), out var id)) throw new AccountFault("invitation_id_invalid");
                    await invitations.RevokeAsync(id, CancellationToken.None);
                    Console.WriteLine("invitation_revoked");
                }
                return 0;
            }
            if (mode != "--formal") return 2;
            var certificatePath = Read("TLS_CERTIFICATE");
            if (string.IsNullOrEmpty(certificatePath) || !Path.IsPathFullyQualified(certificatePath))
            { Console.Error.WriteLine("tls_certificate_required"); return 2; }
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, Read("TLS_PASSWORD"),
                X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new AccountFault("tls_certificate_invalid");
            var smtp = Read("SMTP_HOST") is { Length: > 0 } smtpHost
                ? SmtpOptions.Create(smtpHost, int.TryParse(Read("SMTP_PORT"), out var port) ? port : 0,
                    Read("SMTP_SENDER") ?? string.Empty, Read("SMTP_USERNAME"), Read("SMTP_PASSWORD"), Read("SMTP_TLS") ?? string.Empty)
                : null;
            await using var app = FormalHost.Create(options!, environment, certificate, services =>
            {
                if (smtp is not null) services.AddSingleton<IEmailSender>(new MailKitEmailSender(smtp));
            });
            // Readiness is a gate for all requests, not a startup side effect.
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
                if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new AccountFault("database_migration_required", 503);
            }
            await app.RunAsync();
            return 0;
        }
        catch (AccountFault fault) { Console.Error.WriteLine(fault.Code); return 3; }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine("formal_operation_failed"); return 3; }
    }

    private static string? Read(string suffix) => Environment.GetEnvironmentVariable($"DANGO_{suffix}");
}
