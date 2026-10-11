using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dango.Client.Sync.Accounts;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Email;
using Dango.Server.Hosting;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dango.AccountSyncReview.Tests;

internal sealed class PorchCrudC1Actor(string actor, Guid owner)
{
    internal string Actor { get; } = actor;
    internal string Email { get; } = $"porchc1-{owner:N}-{actor}@example.invalid";
    internal string Password { get; } = "fictional_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal string InvitationProof { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal Guid InvitationId, RegistrationId, UserId, PetId, DefaultRoomId, SessionId;
}
internal sealed record PorchCrudC1Request(int Sequence, int ClientId, string Role, string? Actor, string Method,
    string Path, Guid? SessionId, Guid? RequestId, long? ExpectedVersion, long? SendBefore, long? SendAfter,
    int? Status, long? RoomVersion, string? Operation, string? Terminal);
internal sealed record PorchCrudC1ActorEvidence(string Actor, Guid UserId, Guid PetId, Guid DefaultRoomId,
    Guid SessionId, string Nickname, long ProfileVersion, long ConfigurationVersion);

// No grants, budgets, fixtures or run directories from an earlier stage are reused.
internal sealed class PorchCrudC1HttpsFixture : IAsyncDisposable
{
    private readonly PorchCrudC1TestOwner owner;
    private readonly X509Certificate2 certificate;
    private readonly string capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly object gate = new();
    private readonly List<Client> clients = [];
    private readonly List<PorchCrudC1Request> requests = [];
    private readonly List<Task> tasks = [];
    private readonly List<Task> observers = [];
    private readonly SemaphoreSlim pacing = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly AccountSyncB1bOwnedLifecycle lifecycle = new();
    private long? lastSend;
    private WebApplication? app;
    private Task? disposal;
    private long resourceSequence;
    private int inFlight;
    private string? keyName;
    private CngProvider? keyProvider;
    internal Uri Endpoint { get; }
    internal PorchCrudC1Actor A { get; }
    internal PorchCrudC1Actor B { get; }
    internal Guid? RoomId { get; private set; }
    internal IReadOnlyList<PorchCrudC1Request> Requests { get { lock (gate) return requests.ToArray(); } }
    internal Task[] Tasks { get { lock (gate) return tasks.ToArray(); } }
    internal int ClientsCreated { get { lock (gate) return clients.Count; } }
    internal int DesktopTransports { get { lock (gate) return clients.Count(c => c.Role == "desktop"); } }
    internal async Task DrainObserversAsync()
    {
        Task[] snapshot; lock (gate) snapshot = observers.ToArray();
        await Task.WhenAll(snapshot).WaitAsync(TimeSpan.FromSeconds(20));
    }
    internal const string NativeRelativePath = AccountSyncB1c2EvidenceContract.NativeRelativePath;
    internal const string NativeSha256 = AccountSyncB1c2EvidenceContract.NativeSha256;
    internal static JsonElement Sample => AccountSyncB1c2EvidenceContract.Sample("a", 103);
    internal static string RepositoryRoot() => AccountSyncB1c2HttpsFixture.RepositoryRoot();
    private PorchCrudC1HttpsFixture(PorchCrudC1TestOwner owner, Uri endpoint, X509Certificate2 certificate)
    { this.owner = owner; this.certificate = certificate; Endpoint = endpoint; A = new("a", owner.OwnerId); B = new("b", owner.OwnerId); }

    internal static async Task<PorchCrudC1HttpsFixture> CreateAsync(PorchCrudC1TestOwner owner, Action<PorchCrudC1HttpsFixture> created)
    {
        owner.Budget.CheckAlive();
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        if (port == owner.DatabasePort) throw Reject();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Dango Porch CRUD C1 {owner.OwnerId:N}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var bytes = generated.Export(X509ContentType.Pkcs12); X509Certificate2 cert;
        try { cert = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.DefaultKeySet); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var fixture = new PorchCrudC1HttpsFixture(owner, new($"https://127.0.0.1:{port}/"), cert); created(fixture);
        try
        {
            using (var privateKey = cert.GetRSAPrivateKey())
            {
                if (privateKey is not RSACng cng || cng.Key.IsEphemeral || cng.Key.IsMachineKey || cng.Key.KeyName is not { Length: > 0 } name) throw Reject();
                fixture.keyName = name; fixture.keyProvider = cng.Key.Provider ?? throw Reject();
                if (!CngKey.Exists(name, fixture.keyProvider, CngKeyOpenOptions.None)) throw Reject();
            }
            fixture.Resource("TLSCreated", new { Port = port, CertificateSha256 = Convert.ToHexString(SHA256.HashData(cert.RawData)) });
            if (!FormalServerOptions.TryCreate("dango_backend_b0", owner.ConnectionString, owner.KeyDirectory,
                fixture.Endpoint.AbsoluteUri, RepositoryRoot(), out var options, out _)) throw Reject();
            fixture.app = FormalHost.Create(options!, new(owner.RecoveryEpoch), cert, enableEmailWorker: false,
                enableRoomExpiryWorker: false, enableRoomLiveWorkers: false, configureRequestBoundary: host => host.Use(fixture.ServerBoundary));
            if (fixture.app.Services.GetServices<IHostedService>().Any(s => s is EmailWorker or RoomExpiryWorker or RoomLiveWorker or RoomOutboxWorker)) throw Reject();
            foreach (var actor in new[] { fixture.A, fixture.B })
            {
                owner.Reserve(PorchCrudC1Operation.Invite);
                actor.InvitationId = await fixture.InScopeAsync(s => s.GetRequiredService<InvitationService>()
                    .IssueAsync(actor.Email, actor.InvitationProof, DateTimeOffset.UtcNow.AddHours(1), fixture.lifetime.Token));
            }
            await fixture.lifecycle.StartAsync(fixture.app.StartAsync);
            fixture.Resource("HostStarted", new { Index = 1, Port = port, WorkersDisabled = true }); return fixture;
        }
        catch { owner.Abort(); await fixture.DisposeAsync(); throw; }
    }

    internal async Task RegisterAsync(PorchCrudC1Actor actor)
    {
        using var secrets = new AccountSyncB1c2MemorySecrets(Endpoint);
        using var client = CreateClient("registration", actor, secrets);
        var attempt = RegistrationAttempt.Create(actor.Email, actor.InvitationId, actor.InvitationProof);
        await client.StartRegistrationAsync(attempt, lifetime.Token);
        await InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var challenge = await db.EmailChallenges.AsNoTracking().SingleAsync(c => c.PendingRegistrationId == actor.RegistrationId
                && c.RevokedAt == null && c.ConsumedAt == null, lifetime.Token);
            var proof = services.GetRequiredService<IEmailProofProtector>().Protect(challenge);
            owner.Reserve(PorchCrudC1Operation.Confirm);
            await services.GetRequiredService<RegistrationService>().ConfirmAsync(proof, lifetime.Token); return true;
        });
        var registered = await client.CompleteRegistrationAsync(attempt, actor.Password, Sample, lifetime.Token);
        if (registered.UserId != actor.UserId || registered.PetId != actor.PetId || registered.RoomId != actor.DefaultRoomId) throw Reject();
    }
    internal async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        owner.Budget.CheckAlive(); await using var scope = (app ?? throw Reject()).Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
    internal FormalAccountClient CreateDesktopClient(int index, IAccountSecretStore store)
    { if (index != 1) throw Reject(); return CreateClient("desktop", null, store); }
    internal FormalAccountClient CreateOutsider(IAccountSecretStore store) => CreateClient("outsider", B, store);
    private FormalAccountClient CreateClient(string role, PorchCrudC1Actor? actor, IAccountSecretStore store)
    {
        owner.Budget.CheckAlive(); Client client;
        lock (gate)
        {
            if (clients.Count >= 5 || role is not ("desktop" or "registration" or "outsider")
                || role == "desktop" && clients.Count(c => c.Role == role) >= 2
                || role != "desktop" && (actor is null || clients.Any(c => c.Role == role && c.Actor == actor))) throw Reject();
            client = new(clients.Count + 1, role, actor, new()); clients.Add(client);
        }
        var transport = new HttpClientHandler { CookieContainer = client.Cookies, UseCookies = true, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, supplied, _, _) => supplied is not null && CryptographicOperations.FixedTimeEquals(supplied.RawData, certificate.RawData) };
        return FormalAccountClient.ForTesting(Endpoint, new BoundaryHandler(this, client, transport), client.Cookies, store);
    }
    private async Task ServerBoundary(HttpContext context, RequestDelegate next)
    {
        owner.Budget.CheckAlive(); Client client;
        if (context.Request.Headers["X-Dango-Review-Capability"].Count != 1
            || context.Request.Headers["X-Dango-Review-Capability"].ToString() != capability
            || context.Request.Headers["X-Dango-Review-Client"].Count != 1
            || !int.TryParse(context.Request.Headers["X-Dango-Review-Client"], out var id)) throw Reject();
        lock (gate) client = clients.SingleOrDefault(c => c.Id == id) ?? throw Reject();
        var uri = new Uri(Endpoint, context.Request.Path + context.Request.QueryString);
        _ = PorchCrudC1RequestPolicy.Classify(Endpoint, uri, context.Request.Method, client.Actor?.RegistrationId, RoomId);
        if (uri.AbsolutePath.StartsWith("/api/v1/me", StringComparison.Ordinal)
            || uri.AbsolutePath.StartsWith("/api/v1/rooms", StringComparison.Ordinal) || uri.AbsolutePath == "/api/v1/auth/logout")
        {
            if (client.Actor is not { } actor || actor.UserId == Guid.Empty || client.Session is not { } session
                || context.User.FindFirstValue(ClaimTypes.NameIdentifier) != actor.UserId.ToString("D")
                || context.User.FindFirstValue(FormalHost.SessionClaim) != session.ToString("D")) throw Reject();
        }
        await next(context);
    }
    private sealed class BoundaryHandler(PorchCrudC1HttpsFixture fixture, Client client, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Task<HttpResponseMessage> task; int index;
            lock (fixture.gate)
            {
                if (fixture.tasks.Count >= 128) throw Reject(); index = fixture.requests.Count;
                fixture.requests.Add(new(index + 1, client.Id, client.Role, client.Actor?.Actor, request.Method.Method,
                    request.RequestUri!.PathAndQuery, client.Session, null, null, null, null, null, null, null, null));
                task = SendOwned(request, ct, index); fixture.tasks.Add(task);
            }
            var observer = task.ContinueWith(done =>
            {
                _ = done.Exception;
                lock (fixture.gate) fixture.requests[index] = fixture.requests[index] with { Terminal = done.Status.ToString() };
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            lock (fixture.gate) fixture.observers.Add(observer);
            return task;
        }
        private async Task<HttpResponseMessage> SendOwned(HttpRequestMessage request, CancellationToken caller, int index)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, fixture.lifetime.Token); var ct = linked.Token;
            Interlocked.Increment(ref fixture.inFlight);
            try
            {
                var uri = request.RequestUri ?? throw Reject(); JsonElement? input = null;
                if (request.Content is { } content)
                {
                    await content.LoadIntoBufferAsync(8192, ct);
                    using var doc = JsonDocument.Parse(await content.ReadAsByteArrayAsync(ct), new() { MaxDepth = 16, AllowDuplicateProperties = false }); input = doc.RootElement.Clone();
                }
                if (uri.AbsolutePath == "/api/v1/auth/login")
                {
                    var email = input!.Value.GetProperty("email").GetString();
                    if (client.Session is not null) throw Reject();
                    client.Actor = email == fixture.A.Email && client.Role == "desktop" ? fixture.A
                        : email == fixture.B.Email && client.Role == "outsider" ? fixture.B : throw Reject();
                }
                var operation = PorchCrudC1RequestPolicy.Classify(fixture.Endpoint, uri, request.Method.Method, client.Actor?.RegistrationId, fixture.RoomId);
                fixture.owner.CountHttpRequest(); if (operation is { } mutation) fixture.owner.Reserve(mutation);
                if (client.Role == "registration" && (client.Actor is null || operation is not (null or PorchCrudC1Operation.Start or PorchCrudC1Operation.Complete))) throw Reject();
                if (operation == PorchCrudC1Operation.Start && (input!.Value.GetProperty("email").GetString() != client.Actor!.Email
                    || input.Value.GetProperty("invitationId").GetGuid() != client.Actor.InvitationId)) throw Reject();
                var requestId = input is { } body && body.TryGetProperty("requestId", out var rid) ? rid.GetGuid() : (Guid?)null;
                var expected = input is { } versionBody && versionBody.TryGetProperty("expectedVersion", out var ver) ? ver.GetInt64() : (long?)null;
                lock (fixture.gate) fixture.requests[index] = fixture.requests[index] with
                { Actor = client.Actor?.Actor, SessionId = client.Session, RequestId = requestId, ExpectedVersion = expected, Operation = operation?.ToString() };
                request.Headers.Add("X-Dango-Review-Capability", fixture.capability);
                request.Headers.Add("X-Dango-Review-Client", client.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Task<HttpResponseMessage> sent;
                await fixture.pacing.WaitAsync(ct);
                try
                {
                    while (fixture.lastSend is { } previous)
                    { var delay = TimeSpan.FromMilliseconds(600) - Stopwatch.GetElapsedTime(previous); if (delay <= TimeSpan.Zero) break; await Task.Delay(delay, ct); }
                    ct.ThrowIfCancellationRequested(); fixture.owner.Budget.CheckAlive();
                    var before = Stopwatch.GetTimestamp(); sent = base.SendAsync(request, ct); var after = Stopwatch.GetTimestamp(); fixture.lastSend = after;
                    lock (fixture.gate) fixture.requests[index] = fixture.requests[index] with { SendBefore = before, SendAfter = after };
                }
                finally { fixture.pacing.Release(); }
                var response = await sent;
                try
                {
                    await response.Content.LoadIntoBufferAsync(65536, ct);
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct), new() { MaxDepth = 16, AllowDuplicateProperties = false }); var json = doc.RootElement;
                    if (response.IsSuccessStatusCode)
                    {
                        var actor = client.Actor;
                        if (operation == PorchCrudC1Operation.Start) actor!.RegistrationId = json.GetProperty("id").GetGuid();
                        if (operation == PorchCrudC1Operation.Complete)
                        { actor!.UserId = json.GetProperty("userId").GetGuid(); actor.PetId = json.GetProperty("petId").GetGuid(); actor.DefaultRoomId = json.GetProperty("roomId").GetGuid(); }
                        if (operation == PorchCrudC1Operation.Login)
                        {
                            if (json.GetProperty("userId").GetGuid() != actor!.UserId || actor.SessionId != Guid.Empty) throw Reject();
                            client.Session = actor.SessionId = json.GetProperty("sessionId").GetGuid();
                        }
                        if (operation == PorchCrudC1Operation.Create)
                        { if (fixture.RoomId is not null || client.Actor != fixture.A) throw Reject(); fixture.RoomId = json.GetProperty("roomId").GetGuid(); }
                    }
                    var roomVersion = json.TryGetProperty("version", out var v) ? v.GetInt64() : (long?)null;
                    // Operation receipts use appliedVersion; details use version.
                    if (json.TryGetProperty("appliedVersion", out var av)) roomVersion = av.GetInt64();
                    lock (fixture.gate) fixture.requests[index] = fixture.requests[index] with
                    { Status = (int)response.StatusCode, SessionId = client.Session, RoomVersion = roomVersion };
                    return response;
                }
                catch { response.Dispose(); throw; }
            }
            finally { Interlocked.Decrement(ref fixture.inFlight); }
        }
    }
    internal Task<PorchCrudC1ActorEvidence[]> ReadFinalActorsAsync() => InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>(); var result = new List<PorchCrudC1ActorEvidence>();
        foreach (var actor in new[] { A, B })
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == actor.UserId, lifetime.Token);
            var pet = await db.Pets.AsNoTracking().SingleAsync(p => p.Id == actor.PetId && p.UserId == user.Id, lifetime.Token);
            var session = await db.AuthSessions.AsNoTracking().SingleAsync(s => s.UserId == user.Id, lifetime.Token);
            using var config = JsonDocument.Parse(pet.Configuration);
            if (user.Nickname != (actor.Actor == "a" ? "门廊主人" : "门廊访客") || user.ProfileVersion != 2 || pet.ConfigurationVersion != 1
                || !AppearanceContract.Equivalent(Sample, config.RootElement) || session.Id != actor.SessionId || session.RevokedAt is null
                || session.RecoveryEpoch != owner.RecoveryEpoch) throw Reject();
            result.Add(new(actor.Actor, user.Id, pet.Id, actor.DefaultRoomId, session.Id, user.Nickname, user.ProfileVersion, pet.ConfigurationVersion));
        }
        return result.ToArray();
    });
    internal void Resource(string phase, object facts)
    {
        lock (gate)
        {
            var path = Path.Combine(owner.RunDirectory, "resources.jsonl"); AccountSyncB1c2HttpsFixture.RequireRegular(path);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, owner.OwnerId, Sequence = ++resourceSequence, Phase = phase, Facts = facts });
            if (bytes.Length > 16384 || !File.Exists(path)) throw Reject();
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough); file.Write(bytes); file.WriteByte(10); file.Flush(true);
        }
    }
    public ValueTask DisposeAsync() { lock (gate) return new(disposal ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        owner.Abort(); lifetime.Cancel();
        var report = await AccountSyncB1bCleanup.RunAsync(
            ("HttpTasks", async () => { try { await Task.WhenAll(Tasks).WaitAsync(TimeSpan.FromSeconds(20)); } catch when (Tasks.All(t => t.IsCompleted)) { foreach (var t in Tasks) _ = t.Exception; } }),
            ("Observers", DrainObserversAsync),
            ("HostStop", () => app is null ? Task.CompletedTask : lifecycle.StopAsync(app.StopAsync)),
            ("HostDispose", () => app is null ? Task.CompletedTask : lifecycle.DisposeAsync(() => app.DisposeAsync().AsTask())),
            ("LifecycleDrain", lifecycle.DrainAsync),
            ("HostEvidence", () =>
            {
                var listenerAbsent = !System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == Endpoint.Port);
                Resource("HostDisposed", new { Index = 1, Stopped = lifecycle.IsSuccessful("Stop"), Disposed = lifecycle.IsSuccessful("Dispose"), InFlightRequests = Volatile.Read(ref inFlight), LifecycleTasksSettled = lifecycle.TasksSettledSuccessfully, ListenerAbsent = listenerAbsent });
                if (!listenerAbsent) throw Reject(); return Task.CompletedTask;
            }),
            ("TLS", () => { certificate.Dispose(); var absent = keyName is not null && keyProvider is not null && !CngKey.Exists(keyName, keyProvider, CngKeyOpenOptions.None);
                Resource("TLSDisposed", new { NativeKeyIdentityCaptured = keyName is not null, NativeCleanupConfirmed = absent, NativeProbeFailed = false }); if (!absent) throw Reject(); return Task.CompletedTask; }),
            ("Pacing", () => { if (!Tasks.All(t => t.IsCompleted)) throw Reject(); pacing.Dispose(); lifetime.Dispose(); return Task.CompletedTask; }));
        report.RequireSuccess(); if (Volatile.Read(ref inFlight) != 0 || !lifecycle.TasksSettledSuccessfully) throw Reject();
    }
    private sealed class Client(int id, string role, PorchCrudC1Actor? actor, CookieContainer cookies)
    { internal int Id { get; } = id; internal string Role { get; } = role; internal PorchCrudC1Actor? Actor = actor; internal CookieContainer Cookies { get; } = cookies; internal Guid? Session; }
    private static InvalidOperationException Reject() => new("porch_c1_fixture_rejected");
}
