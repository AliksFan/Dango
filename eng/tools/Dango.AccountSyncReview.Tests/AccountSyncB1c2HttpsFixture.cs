using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Email;
using Dango.Server.Hosting;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dango.AccountSyncReview.Tests;

internal sealed class AccountSyncB1c2Actor(string actor, Guid owner)
{
    internal string Actor { get; } = actor;
    internal string Email { get; } = $"accountsyncb1c2-{owner:N}-{actor}@example.invalid";
    internal string Password { get; } = "fictional_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal string InvitationProof { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal Guid InvitationId, RegistrationId, UserId, PetId, DefaultRoomId;
    internal List<Guid> Sessions { get; } = [];
    internal PetConfigurationSnapshot Snapshot(long version, double size) => new(UserId, PetId, version, AccountSyncB1c2EvidenceContract.Sample(Actor, size));
}

// A new stage owner, two immutable registration actors, and separately bound physical transports.
internal sealed class AccountSyncB1c2HttpsFixture : IAsyncDisposable
{
    private readonly AccountSyncB1c2TestOwner owner;
    private readonly X509Certificate2 certificate;
    private readonly string capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly object gate = new();
    private readonly List<Client> clients = [];
    private readonly List<AccountSyncB1c2Request> requests = [];
    private readonly List<AccountSyncB1c2Phase> phases = [];
    private readonly List<Task> tasks = [];
    private readonly List<Task> operationTasks = [];
    private readonly List<AccountSyncB1cResponseHold> holds = [];
    private readonly SemaphoreSlim pacing = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly AccountSyncB1bOwnedLifecycle lifecycle = new();
    private readonly FormalServerOptions options;
    private long? previousRequestStarted;
    private WebApplication? app;
    private Task? disposal;
    private long resourceSequence;
    private int inFlight;
    private int operationReservations;
    private string? nativeKeyName;
    private CngProvider? nativeProvider;
    internal Uri Endpoint { get; }
    internal AccountSyncB1c2Actor A { get; }
    internal AccountSyncB1c2Actor B { get; }
    internal Task? PreviousPump { get; set; }
    internal AccountSyncB1c2Lifecycle Trace { get; } = new();
    internal AccountSyncB1cResponseHold? CurrentHold { get; private set; }
    internal IReadOnlyList<AccountSyncB1c2Request> Requests { get { lock (gate) return requests.ToArray(); } }
    internal IReadOnlyList<AccountSyncB1c2Phase> Phases { get { lock (gate) return phases.ToArray(); } }
    internal Task[] Tasks { get { lock (gate) return tasks.ToArray(); } }
    internal Task[] OperationTasks { get { lock (gate) return operationTasks.ToArray(); } }
    internal AccountSyncB1cHoldSnapshot[] Holds { get { lock (gate) return holds.Select(h => h.Snapshot()).ToArray(); } }
    internal int ClientsCreated { get { lock (gate) return clients.Count; } }
    internal int DesktopTransports { get { lock (gate) return clients.Count(c => c.Role == "desktop"); } }

    private AccountSyncB1c2HttpsFixture(AccountSyncB1c2TestOwner owner, Uri endpoint, X509Certificate2 certificate)
    {
        this.owner = owner; this.certificate = certificate; Endpoint = endpoint;
        A = new("a", owner.OwnerId); B = new("b", owner.OwnerId);
        if (!FormalServerOptions.TryCreate("dango_backend_b0", owner.ConnectionString, owner.KeyDirectory,
            endpoint.AbsoluteUri, RepositoryRoot(), out var configured, out _)) throw Reject();
        options = configured!;
    }
    internal static string RepositoryRoot()
    {
        DirectoryInfo? root = new(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        return root?.FullName ?? throw Reject();
    }
    internal static async Task<AccountSyncB1c2HttpsFixture> CreateAsync(AccountSyncB1c2TestOwner owner,
        Action<AccountSyncB1c2HttpsFixture> onCreated)
    {
        owner.Budget.CheckAlive(); ArgumentNullException.ThrowIfNull(onCreated);
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        if (port == owner.DatabasePort) throw Reject();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Dango AccountSync B1c1 {owner.OwnerId:N}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var pfx = generated.Export(X509ContentType.Pkcs12); X509Certificate2 certificate;
        try { certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        AccountSyncB1c2HttpsFixture? fixture = null;
        try
        {
            fixture = new(owner, new($"https://127.0.0.1:{port}/"), certificate); onCreated(fixture);
            using (var privateKey = certificate.GetRSAPrivateKey())
            {
                if (privateKey is not RSACng cng || cng.Key.IsEphemeral || cng.Key.IsMachineKey || cng.Key.KeyName is not { Length: > 0 } name) throw Reject();
                fixture.nativeKeyName = name; fixture.nativeProvider = cng.Key.Provider ?? throw Reject();
                if (!CngKey.Exists(name, fixture.nativeProvider, CngKeyOpenOptions.None)) throw Reject();
            }
            fixture.Resource("TLSCreated", new { Port = port, CertificateSha256 = AccountSyncB1c2EvidenceContract.BytesSha256(certificate.RawData) });
            fixture.app = FormalHost.Create(fixture.options, new(owner.RecoveryEpoch), certificate,
                enableEmailWorker: false, enableRoomExpiryWorker: false, enableRoomLiveWorkers: false,
                configureRequestBoundary: host => host.Use(fixture.ServerBoundaryAsync));
            if (fixture.app.Services.GetServices<IHostedService>().Any(s => s is EmailWorker or RoomExpiryWorker or RoomLiveWorker or RoomOutboxWorker)) throw Reject();
            foreach (var actor in new[] { fixture.A, fixture.B })
            {
                owner.Reserve(AccountSyncOperation.Invite);
                actor.InvitationId = await fixture.InScopeAsync(s => s.GetRequiredService<InvitationService>()
                    .IssueAsync(actor.Email, actor.InvitationProof, DateTimeOffset.UtcNow.AddHours(1), fixture.lifetime.Token));
            }
            await fixture.lifecycle.StartAsync(fixture.app.StartAsync);
            fixture.Resource("HostStarted", new { Index = 1, Port = port, WorkersDisabled = true });
            return fixture;
        }
        catch { owner.Abort(); if (fixture is not null) await fixture.DisposeAsync(); else certificate.Dispose(); throw; }
    }
    internal Task RegisterAsync(AccountSyncB1c2Actor actor) => OwnOperation(() => RegisterCoreAsync(actor));
    private async Task RegisterCoreAsync(AccountSyncB1c2Actor actor)
    {
        using var secrets = new AccountSyncB1c2MemorySecrets(Endpoint);
        using var account = CreateClient("registration", 0, actor, null, secrets);
        var attempt = RegistrationAttempt.Create(actor.Email, actor.InvitationId, actor.InvitationProof);
        await account.StartRegistrationAsync(attempt, lifetime.Token);
        await InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var challenge = await db.EmailChallenges.AsNoTracking().SingleAsync(c => c.PendingRegistrationId == actor.RegistrationId && c.RevokedAt == null && c.ConsumedAt == null, lifetime.Token);
            var proof = services.GetRequiredService<IEmailProofProtector>().Protect(challenge);
            owner.Reserve(AccountSyncOperation.Confirm);
            await services.GetRequiredService<RegistrationService>().ConfirmAsync(proof, lifetime.Token); return true;
        });
        var registered = await account.CompleteRegistrationAsync(attempt, actor.Password, AccountSyncB1c2EvidenceContract.Sample(actor.Actor), lifetime.Token);
        if (registered.UserId != actor.UserId || registered.PetId != actor.PetId || registered.RoomId != actor.DefaultRoomId) throw Reject();
    }
    internal Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action) => OwnOperation(() => InScopeCoreAsync(action));
    private async Task<T> InScopeCoreAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        owner.Budget.CheckAlive(); var current = app ?? throw Reject();
        await using var scope = current.Services.CreateAsyncScope(); return await action(scope.ServiceProvider);
    }
    private T OwnOperation<T>(Func<T> create) where T : Task
    {
        T task;
        lock (gate) { if (operationReservations >= 16) throw Reject(); operationReservations++; task = create(); operationTasks.Add(task); }
        _ = task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
    internal FormalAccountClient CreateDesktopClient(int index, IAccountSecretStore store) => CreateClient("desktop", index, null, null, store);
    private FormalAccountClient CreateClient(string role, int desktop, AccountSyncB1c2Actor? actor, Client? source, IAccountSecretStore store)
    {
        owner.Budget.CheckAlive(); Client client;
        lock (gate)
        {
            if (clients.Count >= 12 || role is not ("desktop" or "registration" or "cleanup")
                || role == "desktop" && (desktop is < 1 or > 2 || clients.Count(c => c.Role == role && c.Desktop == desktop) >= 4)
                || role != "desktop" && (actor is null || clients.Count(c => c.Role == role && c.Actor == actor) >= (role == "registration" ? 1 : 2))) throw Reject();
            var cookies = new CookieContainer();
            if (source is not null)
                foreach (Cookie cookie in source.Cookies.GetCookies(Endpoint))
                    cookies.Add(Endpoint, new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
                    { Secure = cookie.Secure, HttpOnly = cookie.HttpOnly, Expires = cookie.Expires, Discard = cookie.Discard, Version = cookie.Version });
            client = new(clients.Count + 1, role, desktop, actor, cookies) { Session = source?.Session };
            clients.Add(client);
        }
        var transport = new HttpClientHandler { CookieContainer = client.Cookies, UseCookies = true, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, supplied, _, _) => supplied is not null && CryptographicOperations.FixedTimeEquals(supplied.RawData, certificate.RawData) };
        return FormalAccountClient.ForTesting(Endpoint, new BoundaryHandler(this, client, transport), client.Cookies, store);
    }
    internal (FormalAccountClient Account, AccountSyncB1c2MemorySecrets Secrets, int SourceId, Guid Session) CopyCleanupClient(int desktop)
    {
        Client source; lock (gate) source = clients.Single(c => c.Role == "desktop" && c.Desktop == desktop && c.Actor == A && c.Session is not null);
        var secrets = new AccountSyncB1c2MemorySecrets(Endpoint);
        try { return (CreateClient("cleanup", desktop, A, source, secrets), secrets, source.Id, source.Session!.Value); }
        catch { secrets.Dispose(); throw; }
    }
    internal void Arm(string name, Guid session)
    {
        owner.Budget.CheckAlive(); lock (gate)
        {
            if (holds.Count >= 2 || holds.Any(h => h.Snapshot().Case == name)) throw Reject();
            CurrentHold = new(); CurrentHold.Arm(name, name == "late-get" ? "GET" : "PATCH", A.UserId, A.PetId, session); holds.Add(CurrentHold);
        }
    }
    internal void Retire(int clientId)
    {
        lock (gate) { var client = clients.Single(c => c.Id == clientId); if (client.Retired || client.Role != "desktop" || client.Actor != A) throw Reject(); client.Retired = true; }
        Mark("main-retired", clientId);
    }
    internal int DesktopClientId(int index, AccountSyncB1c2Actor actor)
    { lock (gate) return clients.Single(c => c.Role == "desktop" && c.Desktop == index && c.Actor == actor && c.Session is not null).Id; }
    internal int ClientId(FormalAccountClient account)
    { lock (gate) return clients.Single(c => ReferenceEquals(c.Cookies, account.Cookies)).Id; }
    internal void Mark(string phase, int? client = null, int? request = null)
    { lock (gate) { if (phases.Count >= 128) throw Reject(); phases.Add(new(phases.Count + 1, phase, client, request)); Trace.Record("Phase", value: phase); } }
    private async Task ServerBoundaryAsync(HttpContext context, Func<Task> next)
    {
        owner.Budget.CheckAlive(); Client client;
        if (context.Request.Headers["X-Dango-Review-Capability"].Count != 1 || context.Request.Headers["X-Dango-Review-Capability"].ToString() != capability
            || context.Request.Headers["X-Dango-Review-Client"].Count != 1 || !int.TryParse(context.Request.Headers["X-Dango-Review-Client"].ToString(), out var id)) throw Reject();
        lock (gate) client = clients.Single(c => c.Id == id);
        var path = context.Request.Path.Value ?? throw Reject();
        _ = AccountSyncRequestPolicy.ClassifyPath(context.Request.Method, path, client.Actor?.RegistrationId);
        if (path.StartsWith("/api/v1/me", StringComparison.Ordinal) || path == "/api/v1/auth/logout")
        {
            if (client.Actor is not { } actor || client.Session is not { } expected
                || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) || user != actor.UserId
                || !Guid.TryParse(context.User.FindFirstValue(FormalHost.SessionClaim), out var session) || session != expected) throw Reject();
        }
        if (path is "/api/v1/auth/login" or "/api/v1/auth/registrations")
        {
            context.Request.EnableBuffering(8192, 8192);
            using var body = await JsonDocument.ParseAsync(context.Request.Body, new() { MaxDepth = 16, AllowDuplicateProperties = false }); context.Request.Body.Position = 0;
            if (client.Actor is null || body.RootElement.GetProperty("email").GetString() != client.Actor.Email
                || path.EndsWith("/registrations", StringComparison.Ordinal) && body.RootElement.GetProperty("invitationId").GetGuid() != client.Actor.InvitationId) throw Reject();
        }
        await next();
    }
    private async Task<PetConfigurationSnapshot?> CaptureAsync(Client client, string path, HttpResponseMessage response, CancellationToken ct, RequestObservation observation)
    {
        if (!response.IsSuccessStatusCode) return null;
        if (path is not ("/api/v1/auth/registrations" or "/api/v1/auth/login" or "/api/v1/me/pet" or "/api/v1/me/pet/configuration")
            && !path.EndsWith("/complete", StringComparison.Ordinal)) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var timeoutObservation = timeout.Token.Register(() => Trace.Record("BodyDeadlineCancelled", observation.Sequence, observation.Operation));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token, timeout.Token);
        observation.BodyToken = deadline.Token;
        await response.Content.LoadIntoBufferAsync(16384, deadline.Token); var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
        if (bytes.Length > 16384) throw Reject();
        using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 16, AllowDuplicateProperties = false }); var json = doc.RootElement;
        var actor = client.Actor ?? throw Reject();
        lock (gate)
        {
            if (path == "/api/v1/auth/registrations") { if (actor.RegistrationId != Guid.Empty) throw Reject(); actor.RegistrationId = json.GetProperty("id").GetGuid(); }
            else if (path == "/api/v1/auth/login")
            {
                var session = json.GetProperty("sessionId").GetGuid();
                if (client.Role != "desktop" || client.Session is not null || session == Guid.Empty || json.GetProperty("userId").GetGuid() != actor.UserId
                    || A.Sessions.Concat(B.Sessions).Contains(session) || actor.Sessions.Count >= 2) throw Reject();
                client.Session = session; actor.Sessions.Add(session);
            }
            else if (path is "/api/v1/me/pet" or "/api/v1/me/pet/configuration")
            {
                var snapshot = new PetConfigurationSnapshot(json.GetProperty("userId").GetGuid(), json.GetProperty("petId").GetGuid(),
                    json.GetProperty("configurationVersion").GetInt64(), AppearanceContract.ValidateAndClone(json.GetProperty("configuration")));
                if (snapshot.UserId != actor.UserId || snapshot.PetId != actor.PetId || snapshot.ConfigurationVersion < 1) throw Reject(); return snapshot;
            }
            else
            {
                if (actor.UserId != Guid.Empty) throw Reject(); actor.UserId = json.GetProperty("userId").GetGuid(); actor.PetId = json.GetProperty("petId").GetGuid(); actor.DefaultRoomId = json.GetProperty("roomId").GetGuid();
                if (actor.UserId == Guid.Empty || actor.PetId == Guid.Empty || actor.DefaultRoomId == Guid.Empty) throw Reject();
            }
        }
        return null;
    }
    private async Task<HttpResponseMessage> SendPacedAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        await pacing.WaitAsync(ct); Task<HttpResponseMessage> actual;
        try
        {
            while (previousRequestStarted is { } previous)
            {
                var delay = TimeSpan.FromMilliseconds(600) - Stopwatch.GetElapsedTime(previous);
                if (delay <= TimeSpan.Zero) break;
                await Task.Delay(delay, ct);
            }
            ct.ThrowIfCancellationRequested(); owner.Budget.CheckAlive();
            actual = send();
            // Record after the actual Send invocation, so setup/lock time can only
            // lengthen the next interval. UTC changes cannot shorten this gate.
            previousRequestStarted = Stopwatch.GetTimestamp();
        }
        finally { pacing.Release(); }
        return await actual;
    }
    private sealed class BoundaryHandler(AccountSyncB1c2HttpsFixture fixture, Client client, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Task<HttpResponseMessage> actual;
            var observation = new RequestObservation { Operation = fixture.Trace.Current };
            lock (fixture.gate) { if (fixture.tasks.Count >= 128) throw Reject(); actual = SendOwnedAsync(request, ct, observation); fixture.tasks.Add(actual); }
            fixture.Trace.ObserveTask(actual, terminal => fixture.Trace.Record("RequestTerminal", observation.Sequence, observation.Operation,
                value: AccountSyncB1c2Lifecycle.Status(terminal)));
            _ = actual.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default); return actual;
        }
        private async Task<HttpResponseMessage> SendOwnedAsync(HttpRequestMessage request, CancellationToken ct, RequestObservation observation)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, fixture.lifetime.Token);
            var callerCancellation = ct;
            ct = linked.Token;
            Interlocked.Increment(ref fixture.inFlight);
            try
            {
                var uri = request.RequestUri ?? throw Reject(); JsonElement? input = null;
                if (request.Content is { } content) { await content.LoadIntoBufferAsync(8192, ct); using var doc = JsonDocument.Parse(await content.ReadAsByteArrayAsync(ct), new() { MaxDepth = 16, AllowDuplicateProperties = false }); input = doc.RootElement.Clone(); }
                if (uri.AbsolutePath == "/api/v1/auth/login")
                {
                    if (client.Role != "desktop" || client.Actor is not null) throw Reject();
                    var email = input!.Value.GetProperty("email").GetString(); client.Actor = email == fixture.A.Email ? fixture.A : email == fixture.B.Email ? fixture.B : throw Reject();
                }
                var operation = AccountSyncRequestPolicy.Classify(fixture.Endpoint, uri, request.Method.Method, client.Actor?.RegistrationId);
                fixture.owner.CountHttpRequest(); if (operation is { } mutation) fixture.owner.Reserve(mutation);
                var expected = operation == AccountSyncOperation.Configuration ? input!.Value.GetProperty("expectedVersion").GetInt64() : (long?)null;
                var hash = operation == AccountSyncOperation.Configuration ? AccountSyncB1c2EvidenceContract.ConfigurationSha256(input!.Value.GetProperty("configuration")) : null;
                int index;
                lock (fixture.gate)
                {
                    if (fixture.requests.Count >= 128) throw Reject(); index = fixture.requests.Count;
                    var ordinal = uri.AbsolutePath == "/api/v1/me/pet" ? ++client.PetReads : (int?)null;
                    fixture.requests.Add(new(index + 1, client.Id, client.Role, client.Actor?.Actor, request.Method.Method, uri.AbsolutePath,
                        client.Actor?.UserId == Guid.Empty ? null : client.Actor?.UserId, client.Actor?.PetId == Guid.Empty ? null : client.Actor?.PetId,
                        client.Session, ordinal, expected, hash, fixture.PreviousPump?.IsCompleted, client.Retired, null, null));
                    observation.Sequence = index + 1;
                    fixture.Trace.Record("RequestRegistered", observation.Sequence, observation.Operation);
                    if (client.Retired) throw Reject();
                }
                using var callerObservation = callerCancellation.Register(() => fixture.Trace.Record("CallerCancelled", observation.Sequence, observation.Operation));
                using var fixtureObservation = fixture.lifetime.Token.Register(() => fixture.Trace.Record("FixtureCancelled", observation.Sequence, observation.Operation));
                request.Headers.Add("X-Dango-Review-Capability", fixture.capability); request.Headers.Add("X-Dango-Review-Client", client.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var response = await fixture.SendPacedAsync(() =>
                {
                    lock (fixture.gate) fixture.requests[index] = fixture.requests[index] with { PreviousPumpSettled = fixture.PreviousPump?.IsCompleted };
                    fixture.Trace.Record("SendBefore", observation.Sequence, observation.Operation);
                    var sent = base.SendAsync(request, ct);
                    fixture.Trace.Record("SendAfter", observation.Sequence, observation.Operation);
                    return sent;
                }, ct);
                lock (fixture.gate) fixture.requests[index] = fixture.requests[index] with { ResponseStatus = (int)response.StatusCode };
                fixture.Trace.Record("HeadersReceived", observation.Sequence, observation.Operation);
                try
                {
                    var candidateHold = client.Actor is { } actor && client.Session is { } candidateSession
                        && fixture.CurrentHold is { } armed && armed.Matches(request.Method.Method, uri.AbsolutePath, actor.UserId, candidateSession)
                        ? armed : null;
                    // Only this precise armed request separates cancellation after an
                    // actual HTTPS response exists; every other body read keeps ct.
                    var snapshot = await fixture.CaptureAsync(client, uri.AbsolutePath, response,
                        candidateHold is null ? ct : fixture.lifetime.Token, observation);
                    lock (fixture.gate) fixture.requests[index] = fixture.requests[index] with { ResponseStatus = (int)response.StatusCode, ResponseVersion = snapshot?.ConfigurationVersion, SessionId = client.Session };
                    fixture.Trace.Record("BodyValidated", observation.Sequence, observation.Operation);
                    if (snapshot is not null && client.Session is { } session && fixture.CurrentHold is { } hold
                        && hold.Matches(request.Method.Method, uri.AbsolutePath, snapshot.UserId, session))
                    {
                        var heldCase = hold.Snapshot().Case;
                        if (!AppearanceContract.Equivalent(snapshot.Configuration,
                            AccountSyncB1c2EvidenceContract.Sample("a", heldCase == "late-get" ? 103 : 111))) throw Reject();
                        return await hold.CaptureAsync(response, snapshot, session, callerCancellation,
                            () => fixture.Mark("actual-response-captured", client.Id, index + 1));
                    }
                    return response;
                }
                catch { response.Dispose(); throw; }
            }
            catch (OperationCanceledException canceled)
            {
                var matched = canceled.CancellationToken == ct || observation.BodyToken is { } body && canceled.CancellationToken == body;
                fixture.Trace.Record("RequestCanceledTokenMatched", observation.Sequence, observation.Operation, value: matched ? "true" : "false");
                throw;
            }
            finally { Interlocked.Decrement(ref fixture.inFlight); }
        }
    }
    internal Task<AccountSyncB1c2ActorEvidence[]> ReadFinalActorsAsync() => InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>(); var result = new List<AccountSyncB1c2ActorEvidence>();
        foreach (var actor in new[] { A, B })
        {
            var user = await db.Users.AsNoTracking().SingleAsync(r => r.Id == actor.UserId, lifetime.Token); var pet = await db.Pets.AsNoTracking().SingleAsync(r => r.Id == actor.PetId && r.UserId == user.Id, lifetime.Token);
            var sessions = await db.AuthSessions.AsNoTracking().Where(r => r.UserId == user.Id).ToListAsync(lifetime.Token);
            var mail = await (from job in db.EmailJobs.AsNoTracking() join challenge in db.EmailChallenges.AsNoTracking() on job.ChallengeId equals challenge.Id where challenge.NormalizedEmail == user.NormalizedEmail select job).SingleAsync(lifetime.Token);
            using var doc = JsonDocument.Parse(pet.Configuration);
            var ids = sessions.Select(s => s.Id.ToString("D")).Order(StringComparer.Ordinal).ToArray();
            if (!ids.SequenceEqual(actor.Sessions.Select(s => s.ToString("D")).Order(StringComparer.Ordinal))
                || sessions.Count != 2 || sessions.Any(s => s.RecoveryEpoch != owner.RecoveryEpoch || s.RevokedAt is null)) throw Reject();
            result.Add(new(actor.Actor, user.Id, pet.Id, actor.DefaultRoomId, user.Nickname, user.ProfileVersion, pet.ConfigurationVersion,
                AppearanceContract.ValidateAndClone(doc.RootElement), ids, sessions.Count, sessions.Count(s => s.RevokedAt is not null), mail.Attempts, mail.State.ToString(), owner.RecoveryEpoch));
        }
        return result.ToArray();
    });
    internal void Resource(string phase, object facts)
    {
        lock (gate)
        {
            var path = Path.Combine(owner.RunDirectory, "resources.jsonl"); RequireRegular(path);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, owner.OwnerId, Sequence = ++resourceSequence, Phase = phase, Facts = facts });
            if (bytes.Length > 16384 || !File.Exists(path)) throw Reject();
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough); file.Write(bytes); file.WriteByte(10); file.Flush(true);
        }
    }
    internal static void RequireRegular(string path)
    {
        for (DirectoryInfo? part = new(Path.GetFullPath(path)); part is not null; part = part.Parent) if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) throw Reject();
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Reject();
    }
    public ValueTask DisposeAsync() { lock (gate) return new(disposal ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        owner.Abort();
        lifetime.Cancel();
        var report = await AccountSyncB1bCleanup.RunAsync(
            ("Holds", async () => { foreach (var hold in holds) await hold.DisposeAsync(); }),
            ("HttpTasks", async () => { try { await Task.WhenAll(Tasks).WaitAsync(TimeSpan.FromSeconds(20)); } catch when (Tasks.All(t => t.IsCompleted)) { foreach (var task in Tasks) _ = task.Exception; } }),
            ("OperationTasks", async () => { try { await Task.WhenAll(OperationTasks).WaitAsync(TimeSpan.FromSeconds(20)); } catch when (OperationTasks.All(t => t.IsCompleted)) { foreach (var task in OperationTasks) _ = task.Exception; } }),
            ("LifecycleObservers", Trace.DrainAsync),
            ("HostStop", () => app is null ? Task.CompletedTask : lifecycle.StopAsync(app.StopAsync)),
            ("HostDispose", () => app is null ? Task.CompletedTask : lifecycle.DisposeAsync(() => app.DisposeAsync().AsTask())),
            ("LifecycleDrain", lifecycle.DrainAsync),
            ("HostEvidence", () => { Resource("HostDisposed", new { Index = 1, Stopped = lifecycle.IsSuccessful("Stop"), Disposed = lifecycle.IsSuccessful("Dispose"), InFlightRequests = Volatile.Read(ref inFlight), LifecycleTasksSettled = lifecycle.TasksSettledSuccessfully }); return Task.CompletedTask; }),
            ("TLS", () => { certificate.Dispose(); var absent = false; var failed = false; try { absent = nativeKeyName is not null && nativeProvider is not null && !CngKey.Exists(nativeKeyName, nativeProvider, CngKeyOpenOptions.None); } catch { failed = true; }
                Resource("TLSDisposed", new { NativeKeyIdentityCaptured = nativeKeyName is not null, NativeCleanupConfirmed = absent, NativeProbeFailed = failed }); if (!absent) throw Reject(); return Task.CompletedTask; }),
            ("Pacing", () => { if (!Tasks.All(t => t.IsCompleted) || !OperationTasks.All(t => t.IsCompleted)) throw Reject(); pacing.Dispose(); lifetime.Dispose(); return Task.CompletedTask; }));
        report.RequireSuccess(); if (Volatile.Read(ref inFlight) != 0 || !lifecycle.TasksSettledSuccessfully) throw Reject();
    }
    private sealed class Client(int id, string role, int desktop, AccountSyncB1c2Actor? actor, CookieContainer cookies)
    { internal int Id { get; } = id; internal string Role { get; } = role; internal int Desktop { get; } = desktop; internal AccountSyncB1c2Actor? Actor = actor; internal CookieContainer Cookies { get; } = cookies; internal Guid? Session; internal int PetReads; internal bool Retired; }
    private sealed class RequestObservation
    {
        internal int Sequence;
        internal AccountSyncB1c2Operation? Operation;
        internal CancellationToken? BodyToken;
    }
    private static InvalidOperationException Reject() => new("accountsync_b1c_fixture_rejected");
}
