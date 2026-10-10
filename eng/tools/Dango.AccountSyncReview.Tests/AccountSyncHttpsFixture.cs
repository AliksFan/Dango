using System.Net;
using System.Net.Http;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dango.Client.Sync.Accounts;
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

// Exclusive composition for one approved journey. No SMTP, trust-store or credential-store access.
internal sealed class AccountSyncHttpsFixture : IAsyncDisposable
{
    private readonly AccountSyncTestOwner owner;
    private readonly X509Certificate2 certificate;
    private readonly WebApplication app;
    private readonly string capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly object gate = new();
    private readonly HashSet<Guid> sessions = [];
    private readonly SemaphoreSlim requestPacing = new(1, 1);
    private DateTimeOffset nextRequest;
    private Guid? registrationId;
    private bool started;
    private long resourceSequence;
    private string? nativeKeyName;
    private CngProvider? nativeProvider;
    private bool disposeStarted;
    private int inFlight;

    private AccountSyncHttpsFixture(AccountSyncTestOwner owner, Uri endpoint, X509Certificate2 certificate)
    {
        this.owner = owner; Endpoint = endpoint; this.certificate = certificate;
        Email = $"accountsync-{owner.OwnerId:N}@example.invalid";
        InvitationProof = NewProof(); Password = "fictional_" + NewProof();
        if (!FormalServerOptions.TryCreate("dango_backend_b0", owner.ConnectionString, owner.KeyDirectory,
            endpoint.AbsoluteUri, TestRepositoryRoot(), out var options, out _)) throw Reject();
        app = FormalHost.Create(options!, new(owner.RecoveryEpoch), certificate,
            enableEmailWorker: false, enableRoomExpiryWorker: false, enableRoomLiveWorkers: false,
            configureRequestBoundary: host => host.Use(ServerBoundaryAsync));
        if (app.Services.GetServices<IHostedService>().Any(s => s is EmailWorker or RoomExpiryWorker or RoomLiveWorker or RoomOutboxWorker))
        { app.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw Reject(); }
    }

    internal Uri Endpoint { get; }
    internal string Email { get; }
    internal string InvitationProof { get; }
    internal string Password { get; }
    internal Guid InvitationId { get; private set; }
    internal AccountIdentity? Identity { get; private set; }
    internal Guid? DefaultRoomId { get; private set; }

    internal static async Task<AccountSyncHttpsFixture> CreateAsync(AccountSyncTestOwner owner)
    {
        owner.Budget.CheckAlive();
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        if (port == owner.DatabasePort) throw Reject();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Dango AccountSync B1a {owner.OwnerId:N}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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
        AccountSyncHttpsFixture? fixture = null;
        try
        {
            fixture = new(owner, new($"https://127.0.0.1:{port}/"), certificate);
            using (var privateKey = certificate.GetRSAPrivateKey())
            {
                if (privateKey is not RSACng cng || cng.Key.IsEphemeral || cng.Key.IsMachineKey || cng.Key.KeyName is not { Length: > 0 } keyName)
                    throw Reject();
                var provider = cng.Key.Provider ?? throw Reject();
                fixture.nativeKeyName = keyName; fixture.nativeProvider = provider;
                if (!CngKey.Exists(keyName, provider, CngKeyOpenOptions.None)) throw Reject();
            }
            fixture.Resource("TLSCreated", new { Port = port, CertificateSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)) });
            owner.Reserve(AccountSyncOperation.Invite);
            fixture.InvitationId = await fixture.InScopeAsync(s => s.GetRequiredService<InvitationService>()
                .IssueAsync(fixture.Email, fixture.InvitationProof, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));
            await fixture.app.StartAsync(); fixture.started = true;
            fixture.Resource("HostStarted", new { Port = port, WorkersDisabled = true });
            return fixture;
        }
        catch
        {
            if (fixture is not null) await fixture.DisposeAsync(); else certificate.Dispose();
            throw;
        }
    }

    internal FormalAccountClient CreateClient(IAccountSecretStore store)
    {
        owner.Budget.CheckAlive();
        var cookies = new CookieContainer();
        var transport = new HttpClientHandler { CookieContainer = cookies, UseCookies = true, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, supplied, _, _) => supplied is not null
                && CryptographicOperations.FixedTimeEquals(supplied.RawData, certificate.RawData) };
        return FormalAccountClient.ForTesting(Endpoint, new BoundaryHandler(this, transport), cookies, store);
    }

    internal async Task ConfirmAsync()
    {
        owner.Budget.CheckAlive();
        var id = registrationId ?? throw Reject();
        await InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            var challenge = await db.EmailChallenges.AsNoTracking().SingleAsync(c => c.PendingRegistrationId == id && c.RevokedAt == null && c.ConsumedAt == null);
            var proof = s.GetRequiredService<IEmailProofProtector>().Protect(challenge);
            owner.Reserve(AccountSyncOperation.Confirm);
            await s.GetRequiredService<RegistrationService>().ConfirmAsync(proof, CancellationToken.None);
            return true;
        });
    }

    internal async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        owner.Budget.CheckAlive(); await using var scope = app.Services.CreateAsyncScope(); return await action(scope.ServiceProvider);
    }

    // The unguessable in-memory capability admits only the controlled handler. Every forwarded
    // mutation has already consumed a durable reservation; no outside caller can bypass it.
    private async Task ServerBoundaryAsync(HttpContext context, Func<Task> next)
    {
        owner.Budget.CheckAlive();
        if (context.Request.Headers["X-Dango-Review-Capability"].Count != 1
            || context.Request.Headers["X-Dango-Review-Capability"].ToString() != capability) throw Reject();
        var path = context.Request.Path.Value ?? throw Reject();
        _ = AccountSyncRequestPolicy.ClassifyPath(context.Request.Method, path, registrationId);
        if (path.StartsWith("/api/v1/me", StringComparison.Ordinal) || path == "/api/v1/auth/logout")
        {
            lock (gate)
                if (Identity is null || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) || user != Identity.UserId
                    || !Guid.TryParse(context.User.FindFirstValue(FormalHost.SessionClaim), out var session) || !sessions.Contains(session)) throw Reject();
        }
        if (path == "/api/v1/auth/login" || path == "/api/v1/auth/registrations")
        {
            context.Request.EnableBuffering(8192, 8192);
            using var body = await JsonDocument.ParseAsync(context.Request.Body, new() { MaxDepth = 16 });
            context.Request.Body.Position = 0;
            if (body.RootElement.GetProperty("email").GetString() != Email) throw Reject();
            if (path.EndsWith("/registrations", StringComparison.Ordinal)
                && body.RootElement.GetProperty("invitationId").GetGuid() != InvitationId) throw Reject();
        }
        await next();
    }

    private async Task CaptureAsync(string path, HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) return;
        if (path is not ("/api/v1/auth/registrations" or "/api/v1/auth/login") && !path.EndsWith("/complete", StringComparison.Ordinal)) return;
        if (response.Content.Headers.ContentLength is > 16384) throw Reject();
        // A successful server commit must retain its attribution even if the caller
        // cancels after receiving headers. This reads only the same bounded response.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await response.Content.LoadIntoBufferAsync(16384, deadline.Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
        if (bytes.Length > 16384) throw Reject();
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 }); var json = document.RootElement;
        lock (gate)
        {
            if (path == "/api/v1/auth/registrations") registrationId = json.GetProperty("id").GetGuid();
            else if (path == "/api/v1/auth/login")
            {
                if (Identity is null || json.GetProperty("userId").GetGuid() != Identity.UserId
                    || !sessions.Add(json.GetProperty("sessionId").GetGuid())) throw Reject();
            }
            else
            {
                var identity = new AccountIdentity(json.GetProperty("userId").GetGuid(), json.GetProperty("petId").GetGuid());
                if (Identity is not null || identity.UserId == Guid.Empty || identity.PetId == Guid.Empty) throw Reject();
                Identity = identity; DefaultRoomId = json.GetProperty("roomId").GetGuid();
            }
        }
    }

    private async Task PaceAsync(CancellationToken ct)
    {
        await requestPacing.WaitAsync(ct);
        try
        {
            var delay = nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(600);
        }
        finally { requestPacing.Release(); }
    }

    private sealed class BoundaryHandler(AccountSyncHttpsFixture fixture, HttpMessageHandler inner) : DelegatingHandler(inner)
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
            await fixture.PaceAsync(ct);
            fixture.owner.Budget.CheckAlive();
            request.Headers.Add("X-Dango-Review-Capability", fixture.capability);
            var response = await base.SendAsync(request, ct);
            try { await fixture.CaptureAsync(uri.AbsolutePath, response); return response; }
            catch { response.Dispose(); fixture.owner.Budget.Abort(); throw; }
        }
    }

    internal void Resource(string phase, object facts)
    {
        // Finally can preserve cleanup evidence after business abort or parent loss. It cannot
        // redirect the fixed protected path or enable another business/resource operation.
        for (DirectoryInfo? part = new(owner.RunDirectory); part is not null; part = part.Parent)
            if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) throw Reject();
        var path = Path.Combine(owner.RunDirectory, "resources.jsonl");
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Reject();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, owner.OwnerId, Sequence = ++resourceSequence, Phase = phase, Facts = facts });
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        file.Write(bytes); file.WriteByte(10); file.Flush(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposeStarted) return; disposeStarted = true;
        owner.Abort();
        var stopped = !started; var appDisposed = false;
        try
        {
            try { if (started) { await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)); stopped = true; } }
            finally { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)); appDisposed = true; }
        }
        finally
        {
            certificate.Dispose();
            var nativeAbsent = false; var nativeProbeFailed = false;
            try { nativeAbsent = nativeKeyName is not null && nativeProvider is not null && !CngKey.Exists(nativeKeyName, nativeProvider, CngKeyOpenOptions.None); }
            catch { nativeProbeFailed = true; }
            Resource("HostDisposed", new { Stopped = stopped, Disposed = appDisposed, InFlightRequests = Volatile.Read(ref inFlight) });
            Resource("TLSDisposed", new { NativeKeyIdentityCaptured = nativeKeyName is not null, NativeCleanupConfirmed = nativeAbsent, NativeProbeFailed = nativeProbeFailed });
            if (!nativeAbsent) throw Reject();
        }
    }

    private static string TestRepositoryRoot()
    {
        for (DirectoryInfo? p = new(AppContext.BaseDirectory); p is not null; p = p.Parent)
            if (File.Exists(Path.Combine(p.FullName, "AGENTS.md")) && File.Exists(Path.Combine(p.FullName, "global.json"))) return p.FullName;
        throw Reject();
    }
    private static string NewProof() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static InvalidOperationException Reject() => new("accountsync_fixture_boundary_rejected");
}
