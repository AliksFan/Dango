using System.Security.Cryptography;
using System.Text.Json;
using Dango.Protocol;

namespace Dango.AccountSyncReview.Tests;

internal static class AccountSyncB1bEvidenceContract
{
    internal static readonly IReadOnlyList<string> Steps = Array.AsReadOnly(new[]
    {
        "cas-primary", "cas-secondary", "cas-loser-follow", "lost-accepted-target-satisfied",
        "lost-accepted-secondary-follow", "lost-accepted-superseded", "newer-secondary-save",
        "superseded-primary-follow", "unforwarded-original-version-retry", "retried-secondary-follow",
        "host-recovery-newest-save", "host-recovery-secondary-follow"
    });

    internal static IReadOnlyList<T> CanonicalArchives<T>(IEnumerable<T> archives, Func<T, string> step, Func<T, string> device)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var archive in archives)
        {
            var name = step(archive); var actor = device(archive);
            if (!Steps.Contains(name, StringComparer.Ordinal) || !map.TryAdd(name, archive)) throw Reject();
            var expected = name switch
            {
                "cas-loser-follow" => actor is "primary" or "secondary" ? actor : throw Reject(),
                "cas-secondary" or "lost-accepted-secondary-follow" or "newer-secondary-save"
                    or "retried-secondary-follow" or "host-recovery-secondary-follow" => "secondary",
                _ => "primary"
            };
            if (actor != expected) throw Reject();
        }
        if (map.Count != Steps.Count) throw Reject();
        return Steps.Select(name => map[name]).ToArray();
    }

    internal static string ConfigurationSha256(JsonElement input)
    {
        var configuration = AppearanceContract.ValidateAndClone(input);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            appearanceSchemaVersion = configuration.GetProperty("appearanceSchemaVersion").GetInt32(),
            bodyColorHex = configuration.GetProperty("bodyColorHex").GetString()!.ToUpperInvariant(),
            eyeColorHex = configuration.GetProperty("eyeColorHex").GetString()!.ToUpperInvariant(),
            eyeSizePercent = Number(configuration, "eyeSizePercent"),
            eyeOvalityPercent = Number(configuration, "eyeOvalityPercent"),
            eyeHeightControlPercent = Number(configuration, "eyeHeightControlPercent"),
            eyeSpacingControlPercent = Number(configuration, "eyeSpacingControlPercent")
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static double Number(JsonElement configuration, string field)
    {
        var value = configuration.GetProperty(field).GetDouble();
        return value == 0 ? 0d : value;
    }
    private static InvalidOperationException Reject() => new("accountsync_b1b_archive_steps_rejected");
}
