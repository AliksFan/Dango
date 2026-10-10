using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Client.Sync.Accounts;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Email;
using Dango.Server.Hosting;
using Dango.Server.Persistence;
using Dango.Server.Pets;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncB1bPatch(int Sequence, string Device, long ExpectedVersion, double EyeSize,
    bool Forwarded, int? ResponseStatus, long? AcceptedVersion, string ConfigurationSha256,
    [property: JsonIgnore] JsonElement Configuration);

// A new owner and two explicitly bound cookie sessions. Planned host recovery never resumes an aborted owner.
internal sealed class AccountSyncB1bHttpsFixture : IAsyncDisposable
{
    private readonly AccountSyncB1bTestOwner owner;
    private readonly X509Certificate2 certificate;
    private readonly string capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly object gate = new();
    private readonly Dictionary<string, Guid> sessions = new(StringComparer.Ordinal);
    private readonly HashSet<string> clients = new(StringComparer.Ordinal);
    private readonly List<AccountSyncB1bPatch> patches = [];
    private readonly List<AccountSyncB1bOwnedLifecycle> lifecycles = [];
    private readonly SemaphoreSlim pacing = new(1, 1);
    private readonly FormalServerOptions options;
    private WebApplication? app;
    private DateTimeOffset nextRequest;
    private Guid? registrationId;
    private int hostIndex;
    private bool hostStarted, recoveryStopped;
    private AccountSyncB1bOwnedLifecycle? hostLifecycle;
    private Task? hostCleanup, disposal;
    private long resourceSequence;
    private int inFlight;
    private string? nativeKeyName;
    private CngProvider? nativeProvider;

    private AccountSyncB1bHttpsFixture(AccountSyncB1bTestOwner owner, Uri endpoint, X509Certificate2 certificate)
    {
        this.owner = owner; this.certificate = certificate; Endpoint = endpoint;
        Email = $"accountsyncb1b-{owner.OwnerId:N}@example.invalid";
        InvitationProof = NewProof(); Password = "fictional_" + NewProof();
        if (!FormalServerOptions.TryCreate("dango_backend_b0", owner.ConnectionString, owner.KeyDirectory,
            endpoint.AbsoluteUri, RepositoryRoot(), out var configured, out _)) throw Reject();
        options = configured!;
    }

    internal AccountSyncB1bFaultControl Faults { get; } = new();
    internal Uri Endpoint { get; }
    internal string Email { get; }
    internal string InvitationProof { get; }
    internal string Password { get; }
    internal Guid InvitationId { get; private set; }
    internal AccountIdentity? Identity { get; private set; }
    internal Guid? DefaultRoomId { get; private set; }
    internal IReadOnlyList<AccountSyncB1bPatch> PatchSnapshot() { lock (gate) return patches.ToArray(); }

    internal static async Task<AccountSyncB1bHttpsFixture> CreateAsync(AccountSyncB1bTestOwner owner,
        Action<AccountSyncB1bHttpsFixture> onCreated)
    {
        owner.Budget.CheckAlive();
        ArgumentNullException.ThrowIfNull(onCreated);
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        if (port == owner.DatabasePort) throw Reject();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Dango AccountSync B1b1 {owner.OwnerId:N}", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var pfx = generated.Export(X509ContentType.Pkcs12);
        X509Certificate2 certificate;
        try { certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        AccountSyncB1bHttpsFixture? fixture = null;
        try
        {
            fixture = new(owner, new($"https://127.0.0.1:{port}/"), certificate);
            // Publish ownership before the first awaited initialization or lifecycle operation.
            onCreated(fixture);
            using (var privateKey = certificate.GetRSAPrivateKey())
            {
                if (privateKey is not RSACng cng || cng.Key.IsEphemeral || cng.Key.IsMachineKey
                    || cng.Key.KeyName is not { Length: > 0 } name) throw Reject();
                fixture.nativeKeyName = name; fixture.nativeProvider = cng.Key.Provider ?? throw Reject();
                if (!CngKey.Exists(name, fixture.nativeProvider, CngKeyOpenOptions.None)) throw Reject();
            }
            fixture.Resource("TLSCreated", new { Port = port, CertificateSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)) });
            fixture.CreateHost();
            owner.Reserve(AccountSyncOperation.Invite);
            fixture.InvitationId = await fixture.InScopeAsync(s => s.GetRequiredService<InvitationService>()
                .IssueAsync(fixture.Email, fixture.InvitationProof, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));
            await fixture.StartHostAsync();
            return fixture;
        }
        catch
        {
            owner.Abort();
            if (fixture is not null) await fixture.DisposeAsync(); else certificate.Dispose();
            throw;
        }
    }

    private void CreateHost()
    {
        owner.Budget.CheckAlive();
        if (app is not null || hostIndex >= 2) throw Reject();
        app = FormalHost.Create(options, new(owner.RecoveryEpoch), certificate,
            configure: services => services.AddScoped<IPetConfigurationCheckpoint>(provider =>
                Faults.CreateCheckpoint(provider.GetRequiredService<DangoDbContext>())),
            enableEmailWorker: false, enableRoomExpiryWorker: false, enableRoomLiveWorkers: false,
            configureRequestBoundary: host => host.Use(ServerBoundaryAsync));
        hostLifecycle = new(); lifecycles.Add(hostLifecycle); hostCleanup = null;
        hostIndex++;
        if (app.Services.GetServices<IHostedService>().Any(service =>
            service is EmailWorker or RoomExpiryWorker or RoomLiveWorker or RoomOutboxWorker)) throw Reject();
    }

    private async Task StartHostAsync()
    {
        owner.Budget.CheckAlive();
        var current = app ?? throw Reject();
        await (hostLifecycle ?? throw Reject()).StartAsync(current.StartAsync); hostStarted = true;
        Resource("HostStarted", new { Index = hostIndex, Port = Endpoint.Port, WorkersDisabled = true });
    }

    internal FormalAccountClient CreateClient(string device, IAccountSecretStore store)
    {
        owner.Budget.CheckAlive();
        RequireDevice(device);
        lock (gate) if (!clients.Add(device)) throw Reject();
        var cookies = new CookieContainer();
        var transport = new HttpClientHandler
        {
            CookieContainer = cookies, UseCookies = true, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, supplied, _, _) => supplied is not null
                && CryptographicOperations.FixedTimeEquals(supplied.RawData, certificate.RawData)
        };
        return FormalAccountClient.ForTesting(Endpoint, new BoundaryHandler(this, device, transport), cookies, store);
    }

    internal async Task ConfirmAsync()
    {
        owner.Budget.CheckAlive();
        var id = registrationId ?? throw Reject();
        await InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var challenge = await db.EmailChallenges.AsNoTracking().SingleAsync(c =>
                c.PendingRegistrationId == id && c.RevokedAt == null && c.ConsumedAt == null);
            var proof = services.GetRequiredService<IEmailProofProtector>().Protect(challenge);
            owner.Reserve(AccountSyncOperation.Confirm);
            await services.GetRequiredService<RegistrationService>().ConfirmAsync(proof, CancellationToken.None);
            return true;
        });
    }

    internal async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        owner.Budget.CheckAlive();
        var current = app ?? throw Reject();
        await using var scope = current.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    internal async Task StopHostForRecoveryAsync()
    {
        owner.Budget.CheckAlive(); Faults.AssertSettled();
        if (hostIndex != 1 || !hostStarted || recoveryStopped || Volatile.Read(ref inFlight) != 0) throw Reject();
        await StopAndDisposeHostAsync(); recoveryStopped = true;
    }

    internal async Task RebuildHostAsync()
    {
        owner.Budget.CheckAlive();
        if (!recoveryStopped || hostIndex != 1 || app is not null || Volatile.Read(ref inFlight) != 0) throw Reject();
        CreateHost(); await StartHostAsync();
    }

    private async Task ServerBoundaryAsync(HttpContext context, Func<Task> next)
    {
        owner.Budget.CheckAlive();
        if (context.Request.Headers["X-Dango-Review-Capability"].Count != 1
            || context.Request.Headers["X-Dango-Review-Capability"].ToString() != capability
            || context.Request.Headers["X-Dango-Review-Device"].Count != 1) throw Reject();
        var device = context.Request.Headers["X-Dango-Review-Device"].ToString(); RequireDevice(device);
        var path = context.Request.Path.Value ?? throw Reject();
        _ = AccountSyncRequestPolicy.ClassifyPath(context.Request.Method, path, registrationId);
        if (path.StartsWith("/api/v1/me", StringComparison.Ordinal) || path == "/api/v1/auth/logout")
        {
            lock (gate)
                if (Identity is null || !sessions.TryGetValue(device, out var expectedSession)
                    || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) || user != Identity.UserId
                    || !Guid.TryParse(context.User.FindFirstValue(FormalHost.SessionClaim), out var session) || session != expectedSession) throw Reject();
        }
        if (path.StartsWith("/api/v1/auth/registrations", StringComparison.Ordinal)
            && device != AccountSyncB1bFaultControl.PrimaryDevice) throw Reject();
        if (path is "/api/v1/auth/login" or "/api/v1/auth/registrations")
        {
            context.Request.EnableBuffering(8192, 8192);
            using var body = await JsonDocument.ParseAsync(context.Request.Body, new() { MaxDepth = 16, AllowDuplicateProperties = false });
            context.Request.Body.Position = 0;
            if (body.RootElement.GetProperty("email").GetString() != Email) throw Reject();
            if (path.EndsWith("/registrations", StringComparison.Ordinal)
                && body.RootElement.GetProperty("invitationId").GetGuid() != InvitationId) throw Reject();
        }
        await next();
    }

    private async Task CaptureAsync(string device, string path, HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) return;
        if (path is not ("/api/v1/auth/registrations" or "/api/v1/auth/login" or "/api/v1/me/pet/configuration")
            && !path.EndsWith("/complete", StringComparison.Ordinal)) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await response.Content.LoadIntoBufferAsync(16384, deadline.Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
        if (bytes.Length > 16384) throw Reject();
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16, AllowDuplicateProperties = false });
        var json = document.RootElement;
        lock (gate)
        {
            if (path == "/api/v1/auth/registrations")
            {
                if (registrationId is not null) throw Reject();
                registrationId = json.GetProperty("id").GetGuid();
            }
            else if (path == "/api/v1/auth/login")
            {
                var session = json.GetProperty("sessionId").GetGuid();
                if (Identity is null || json.GetProperty("userId").GetGuid() != Identity.UserId || session == Guid.Empty
                    || sessions.ContainsKey(device) || sessions.Values.Contains(session)) throw Reject();
                sessions.Add(device, session);
            }
            else if (path == "/api/v1/me/pet/configuration")
            {
                if (Identity is null || json.GetProperty("userId").GetGuid() != Identity.UserId
                    || json.GetProperty("petId").GetGuid() != Identity.PetId
                    || json.GetProperty("configurationVersion").GetInt64() < 1) throw Reject();
                _ = AppearanceContract.ValidateAndClone(json.GetProperty("configuration"));
            }
            else
            {
                var identity = new AccountIdentity(json.GetProperty("userId").GetGuid(), json.GetProperty("petId").GetGuid());
                var room = json.GetProperty("roomId").GetGuid();
                if (Identity is not null || identity.UserId == Guid.Empty || identity.PetId == Guid.Empty || room == Guid.Empty) throw Reject();
                Identity = identity; DefaultRoomId = room;
            }
        }
    }

    private async Task PaceAsync(CancellationToken ct)
    {
        await pacing.WaitAsync(ct);
        try
        {
            var delay = nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(600);
        }
        finally { pacing.Release(); }
    }

    private sealed class BoundaryHandler(AccountSyncB1bHttpsFixture fixture, string device, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref fixture.inFlight);
            try { return await SendOwnedAsync(request, ct); }
            finally { Interlocked.Decrement(ref fixture.inFlight); }
        }

        private async Task<HttpResponseMessage> SendOwnedAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri ?? throw Reject();
            var operation = AccountSyncRequestPolicy.Classify(fixture.Endpoint, uri, request.Method.Method, fixture.registrationId);
            fixture.owner.CountHttpRequest();
            if (operation is { } mutation) fixture.owner.Reserve(mutation);
            int? patchIndex = null;
            if (operation == AccountSyncOperation.Configuration)
            {
                var content = request.Content ?? throw Reject();
                await content.LoadIntoBufferAsync(8192, ct);
                using var document = JsonDocument.Parse(await content.ReadAsByteArrayAsync(ct), new() { MaxDepth = 16, AllowDuplicateProperties = false });
                var expected = document.RootElement.GetProperty("expectedVersion").GetInt64();
                var appearance = AppearanceContract.ValidateAndClone(document.RootElement.GetProperty("configuration"));
                lock (fixture.gate)
                {
                    patchIndex = fixture.patches.Count;
                    fixture.patches.Add(new(patchIndex.Value + 1, device, expected,
                        appearance.GetProperty("eyeSizePercent").GetDouble(), false, null, null,
                        AccountSyncB1bEvidenceContract.ConfigurationSha256(appearance), appearance.Clone()));
                }
            }
            await fixture.PaceAsync(ct); fixture.owner.Budget.CheckAlive();
            await fixture.Faults.BeforeForwardAsync(device, request.Method.Method, uri.AbsolutePath, ct);
            request.Headers.Add("X-Dango-Review-Capability", fixture.capability);
            request.Headers.Add("X-Dango-Review-Device", device);
            if (patchIndex is { } forwarded) lock (fixture.gate)
                fixture.patches[forwarded] = fixture.patches[forwarded] with { Forwarded = true };
            var response = await base.SendAsync(request, ct);
            try
            {
                await fixture.CaptureAsync(device, uri.AbsolutePath, response);
                if (patchIndex is { } answered)
                {
                    long? accepted = null;
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
                        accepted = document.RootElement.GetProperty("configurationVersion").GetInt64();
                    }
                    lock (fixture.gate) fixture.patches[answered] = fixture.patches[answered] with
                    { ResponseStatus = (int)response.StatusCode, AcceptedVersion = accepted };
                }
            }
            catch { response.Dispose(); fixture.owner.Abort(); throw; }
            // Only a real successful response already captured above can be deliberately lost.
            try
            {
                await fixture.Faults.AfterResponseAsync(device, request.Method.Method, uri.AbsolutePath, response, ct);
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }

    internal void Resource(string phase, object facts)
    {
        lock (gate)
        {
            for (DirectoryInfo? part = new(owner.RunDirectory); part is not null; part = part.Parent)
                if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) throw Reject();
            var path = Path.Combine(owner.RunDirectory, "resources.jsonl");
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Reject();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, owner.OwnerId, Sequence = ++resourceSequence, Phase = phase, Facts = facts });
            if (bytes.Length > 16384) throw Reject();
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            file.Write(bytes); file.WriteByte(10); file.Flush(true);
        }
    }

    private Task StopAndDisposeHostAsync()
    {
        lock (gate)
        {
            if (app is null) return Task.CompletedTask;
            return hostCleanup ??= StopAndDisposeHostCoreAsync(app, hostLifecycle ?? throw Reject(), hostIndex);
        }
    }

    private async Task StopAndDisposeHostCoreAsync(WebApplication current, AccountSyncB1bOwnedLifecycle lifecycle, int index)
    {
        var report = await AccountSyncB1bCleanup.RunAsync(
            ("HostStop", () => lifecycle.StopAsync(current.StopAsync)),
            ("HostDispose", () => lifecycle.DisposeAsync(() => current.DisposeAsync().AsTask())),
            ("HostLifecycleDrain", lifecycle.DrainAsync));
        var stopped = lifecycle.IsSuccessful("Stop"); var disposed = lifecycle.IsSuccessful("Dispose");
        var settled = lifecycle.TasksSettledSuccessfully;
        var recorded = await AccountSyncB1bCleanup.RunAsync(("HostResourceEvidence", () =>
        {
            Resource("HostDisposed", new { Index = index, Stopped = stopped, Disposed = disposed,
                InFlightRequests = Volatile.Read(ref inFlight), LifecycleTasksSettled = settled });
            return Task.CompletedTask;
        }));
        if (disposed) { app = null; hostStarted = false; }
        report.RequireSuccess(); recorded.RequireSuccess();
        if (!settled || Volatile.Read(ref inFlight) != 0) throw Reject();
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        owner.Abort();
        var cleanup = await AccountSyncB1bCleanup.RunAsync(
            ("FaultControls", () => Faults.DisposeAsync().AsTask()),
            ("CurrentHost", StopAndDisposeHostAsync),
            ("AllHostLifecycleTasks", async () =>
            {
                var drains = await AccountSyncB1bCleanup.RunAsync(lifecycles.Select((lifecycle, index) =>
                    ($"Host{index + 1}Drain", (Func<Task>)lifecycle.DrainAsync)).ToArray());
                drains.RequireSuccess();
                if (lifecycles.Any(lifecycle => !lifecycle.TasksSettledSuccessfully)) throw Reject();
            }),
            ("TLS", () =>
            {
                certificate.Dispose();
                var absent = false; var probeFailed = false;
                try { absent = nativeKeyName is not null && nativeProvider is not null && !CngKey.Exists(nativeKeyName, nativeProvider, CngKeyOpenOptions.None); }
                catch { probeFailed = true; }
                Resource("TLSDisposed", new { NativeKeyIdentityCaptured = nativeKeyName is not null, NativeCleanupConfirmed = absent, NativeProbeFailed = probeFailed });
                if (!absent) throw Reject();
                return Task.CompletedTask;
            }),
            ("RequestPacing", () => { pacing.Dispose(); return Task.CompletedTask; }));
        cleanup.RequireSuccess();
        if (Volatile.Read(ref inFlight) != 0) throw Reject();
    }

    internal IReadOnlyList<AccountSyncB1bLifecycleTask> LifecycleSnapshot()
    {
        lock (gate) return lifecycles.SelectMany(lifecycle => lifecycle.Snapshot()).ToArray();
    }

    private static void RequireDevice(string device)
    {
        if (device is not (AccountSyncB1bFaultControl.PrimaryDevice or AccountSyncB1bFaultControl.SecondaryDevice)) throw Reject();
    }
    private static string RepositoryRoot()
    {
        for (DirectoryInfo? part = new(AppContext.BaseDirectory); part is not null; part = part.Parent)
            if (File.Exists(Path.Combine(part.FullName, "AGENTS.md")) && File.Exists(Path.Combine(part.FullName, "global.json"))) return part.FullName;
        throw Reject();
    }
    private static string NewProof() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static InvalidOperationException Reject() => new("accountsync_b1b_fixture_boundary_rejected");
}
