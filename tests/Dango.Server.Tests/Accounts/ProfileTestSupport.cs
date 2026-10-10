using System.Net;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Pets;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dango.Server.Tests.Accounts;

// This helper is exclusive to the seven Profile cases. Shared B1/Pet helpers remain unchanged.
internal static class ProfileTestSupport
{
    internal static async Task<ProfileFixtureScope<FormalTestFixture>> CreateFixtureAsync(bool startHttp = false, TimeProvider? clock = null)
    {
        var owner = ProfileTestOwner.Require();
        var target = ProfileTargetFacts.ReadApprovedEnvironment();
        var scope = await owner.CreateFixtureAsync(startHttp, () => FormalTestFixture.CreateAsync(startHttp: startHttp, clock: clock));
        try
        {
            await scope.Fixture.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<DangoDbContext>();
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT current_database(), current_user, current_setting('data_directory'), current_setting('listen_addresses'), current_setting('port')::integer", connection);
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync() || reader.GetString(0) != "dango_backend_b0" || reader.GetString(1) != "dango_b0_owner"
                    || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(reader.GetString(2))).Equals(target.DataDirectory, StringComparison.OrdinalIgnoreCase)
                    || reader.GetString(3) != "127.0.0.1" || reader.GetInt32(4) != target.Port
                    || services.GetRequiredService<AccountEnvironment>().RecoveryEpoch != target.RecoveryEpoch)
                    throw new InvalidOperationException("profile_fixture_target_rejected");
            });
            return scope;
        }
        catch { await scope.DisposeAsync(); throw; }
    }

    internal static async Task<PetTestSupport.Account> RegisterAsync(FormalTestFixture fixture)
    {
        var owner = ProfileTestOwner.Require();
        var email = $"profilea-{owner.OwnerId:N}-{Guid.NewGuid():N}@example.invalid";
        var invitationProof = B1InputTests.NewProof();
        owner.Reserve(ProfileAttemptOperation.Invite);
        var invitation = await fixture.InviteAsync(email, invitationProof);
        var resume = B1InputTests.NewProof(); var request = Guid.NewGuid();
        owner.Reserve(ProfileAttemptOperation.Start);
        var pending = await fixture.InScopeAsync(s => s.GetRequiredService<RegistrationService>().StartAsync(
            email, invitation, invitationProof, request, resume, CancellationToken.None));
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            var challenge = await db.EmailChallenges.AsNoTracking().SingleAsync(c => c.PendingRegistrationId == pending.Id && c.RevokedAt == null);
            var proof = s.GetRequiredService<IEmailProofProtector>().Protect(challenge);
            owner.Reserve(ProfileAttemptOperation.Confirm);
            await s.GetRequiredService<RegistrationService>().ConfirmAsync(proof, CancellationToken.None);
        });
        var password = B1PostgresTests.NewPassword(); var completion = Guid.NewGuid();
        owner.Reserve(ProfileAttemptOperation.Complete);
        var registered = await fixture.InScopeAsync(s => s.GetRequiredService<RegistrationService>().CompleteAsync(
            pending.Id, resume, completion, password, B1InputTests.Parse(B1InputTests.ValidAppearance), CancellationToken.None));
        owner.Reserve(ProfileAttemptOperation.Login);
        var login = await fixture.InScopeAsync(s => s.GetRequiredService<SessionService>().LoginAsync(email, password, false, CancellationToken.None));
        return new(registered.UserId, registered.PetId, registered.RoomId, login.SessionId, email, password);
    }

    internal static Task<AccountProfileSnapshot> UpdateAsync(AccountProfileService service, Guid userId, Guid sessionId,
        AccountProfileUpdate update, CancellationToken ct)
    {
        ProfileTestOwner.Require().Reserve(ProfileAttemptOperation.DirectProfileUpdate);
        return service.UpdateAsync(userId, sessionId, update, ct);
    }

    internal static Task RevokeAsync(SessionService service, Guid userId, Guid sessionId, CancellationToken ct)
    {
        ProfileTestOwner.Require().Reserve(ProfileAttemptOperation.SessionRevoke);
        return service.RevokeAsync(userId, sessionId, ct);
    }

    internal static HttpClient CreateClient(FormalTestFixture fixture, out CookieContainer cookies)
    {
        cookies = new CookieContainer();
        var owner = ProfileTestOwner.Require();
        var handler = fixture.CreatePinnedHandler(cookies);
        handler.AllowAutoRedirect = false;
        return new HttpClient(new ProfileHttpBoundary(handler, fixture.Endpoint, owner.CountHttpRequest, owner.Reserve))
            { BaseAddress = fixture.Endpoint, Timeout = TimeSpan.FromSeconds(20) };
    }

    internal sealed class ProfileHttpBoundary(HttpMessageHandler inner, Uri endpoint, Action countRequest,
        Action<ProfileAttemptOperation> reserve) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri;
            if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps
                || !IPAddress.TryParse(endpoint.Host, out var address) || !IPAddress.IsLoopback(address)
                || endpoint.Scheme != Uri.UriSchemeHttps || uri.Port != endpoint.Port
                || !uri.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase)
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("profile_http_origin_rejected");
            var path = uri.AbsolutePath;
            ProfileAttemptOperation? operation = (request.Method.Method, path) switch
            {
                ("POST", "/api/v1/auth/login") => ProfileAttemptOperation.Login,
                ("PATCH", "/api/v1/me/profile") => ProfileAttemptOperation.HttpProfilePatch,
                ("POST", "/api/v1/auth/logout") => ProfileAttemptOperation.HttpLogout,
                ("GET", "/api/v1/auth/request-protection" or "/api/v1/me/profile") => null,
                _ => throw new InvalidOperationException("profile_http_route_rejected")
            };
            countRequest();
            if (operation is { } mutation) reserve(mutation);
            // Do not parse or change input/CSRF/Origin: rejection evidence must come from FormalHost.
            return base.SendAsync(request, ct);
        }
    }
}

internal sealed record ProfileTargetFacts(string DataDirectory, int Port, Guid RecoveryEpoch)
{
    internal static ProfileTargetFacts ReadApprovedEnvironment()
    {
        var run = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "BackendDevelopment", "run20261008b0");
        var data = Path.Combine(run, "pgdata"); var keys = Path.Combine(run, "b1tests-keys");
        var suppliedData = Environment.GetEnvironmentVariable("DANGO_BACKEND_TEST_DATA_DIRECTORY");
        var suppliedKeys = Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_KEY_DIRECTORY");
        if (string.IsNullOrEmpty(suppliedData) || string.IsNullOrEmpty(suppliedKeys)
            || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(suppliedData)).Equals(data, StringComparison.OrdinalIgnoreCase)
            || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(suppliedKeys)).Equals(keys, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_RECOVERY_EPOCH"), out var epoch) || epoch == Guid.Empty)
            throw new InvalidOperationException("profile_fixture_environment_rejected");
        CheckDirectory(data); CheckDirectory(keys);
        using var state = ReadState(Path.Combine(run, "state.json"));
        using var recovery = ReadState(Path.Combine(run, "b1test-state.json"));
        var facts = state.RootElement;
        var port = facts.GetProperty("Port").GetInt32();
        if (facts.GetProperty("Version").GetInt32() != 1 || facts.GetProperty("RunName").GetString() != "run20261008b0"
            || facts.GetProperty("Database").GetString() != "dango_backend_b0" || facts.GetProperty("Owner").GetString() != "dango_b0_owner"
            || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(facts.GetProperty("DataDirectory").GetString()!)).Equals(data, StringComparison.OrdinalIgnoreCase)
            || port is < 1 or > 65535 || recovery.RootElement.GetProperty("Version").GetInt32() != 1
            || recovery.RootElement.GetProperty("RecoveryEpoch").GetGuid() != epoch)
            throw new InvalidOperationException("profile_fixture_state_rejected");
        return new(data, port, epoch);
    }

    private static JsonDocument ReadState(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 8192 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("profile_fixture_state_rejected");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
    private static void CheckDirectory(string path)
    {
        for (DirectoryInfo? part = new(path); part is not null; part = part.Parent)
            if (!part.Exists || (part.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("profile_fixture_directory_rejected");
    }
}
