using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Email;
using Dango.Server.Hosting;
using Dango.Server.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.Accounts;

internal sealed class FormalTestFixture : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly X509Certificate2 certificate;
    private readonly bool started;
    private readonly string? resourceReceiptPath;

    private FormalTestFixture(WebApplication app, X509Certificate2 certificate, Uri endpoint, bool started, string? resourceReceiptPath)
    {
        this.app = app;
        this.certificate = certificate;
        this.started = started;
        Endpoint = endpoint;
        this.resourceReceiptPath = resourceReceiptPath;
    }

    public Uri Endpoint { get; }

    public static async Task<FormalTestFixture> CreateAsync(bool startHttp = false, IRegistrationCheckpoint? checkpoint = null,
        TimeProvider? clock = null, IEmailSender? sender = null, bool enableEmailWorker = false,
        AccountEnvironment? environment = null, Action<IServiceCollection>? configure = null, bool requireB5AApproval = false,
        bool requireB5BApproval = false, bool requireB5CApproval = false)
    {
        Assert.IsTrue(new[] { requireB5AApproval, requireB5BApproval, requireB5CApproval }.Count(value => value) <= 1,
            "Client integration stages cannot share approval.");
        Assert.IsFalse(requireB5CApproval && enableEmailWorker, "B5C cannot enable an email worker.");
        var target = await ValidateTargetAsync(requireB5AApproval, requireB5BApproval, requireB5CApproval);
        var epochText = Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_RECOVERY_EPOCH");
        Assert.IsTrue(Guid.TryParse(epochText, out var epoch) && epoch != Guid.Empty,
            "The authorized runner must supply the isolated B1 recovery epoch.");
        var keyDirectory = Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_KEY_DIRECTORY");
        Assert.IsFalse(string.IsNullOrEmpty(keyDirectory));
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(target.DataDirectory)!, "b1tests-keys"), Path.GetFullPath(keyDirectory!), true);
        if (requireB5BApproval || requireB5CApproval)
        {
            var keys = new DirectoryInfo(keyDirectory!);
            Assert.IsTrue(keys.Exists && (keys.Attributes & FileAttributes.ReparsePoint) == 0,
                "The room client stage must reuse the existing key ring.");
            var statePath = Path.Combine(Path.GetDirectoryName(target.DataDirectory)!, "b1test-state.json");
            var stateFile = new FileInfo(statePath);
            Assert.IsTrue(stateFile.Exists && stateFile.Length is > 0 and <= 8192
                && (stateFile.Attributes & FileAttributes.ReparsePoint) == 0);
            using var state = JsonDocument.Parse(await File.ReadAllBytesAsync(statePath));
            Assert.AreEqual(1, state.RootElement.GetProperty("Version").GetInt32());
            Assert.AreEqual(epoch, state.RootElement.GetProperty("RecoveryEpoch").GetGuid(),
                "The room client stage cannot invent a replacement recovery epoch.");
        }
        var endpoint = new Uri($"https://127.0.0.1:{FindLoopbackPort()}/");
        var repository = FindRepository();
        var resourceReceiptPath = requireB5CApproval ? B5CResourcePath(repository, endpoint.Port) : null;
        Assert.IsTrue(FormalServerOptions.TryCreate("dango_backend_b0", target.ConnectionString, keyDirectory!,
            endpoint.AbsoluteUri, repository, out var options, out _));
        var certificate = CreateCertificate();
        WebApplication? app = null;
        try
        {
            app = FormalHost.Create(options!, environment ?? new AccountEnvironment(epoch), certificate, services =>
            {
                if (checkpoint is not null) services.Replace(ServiceDescriptor.Singleton(checkpoint));
                if (clock is not null) services.Replace(ServiceDescriptor.Singleton(clock));
                if (sender is not null) services.Replace(ServiceDescriptor.Singleton(sender));
                configure?.Invoke(services);
            }, enableEmailWorker);
            if (startHttp) await app.StartAsync();
            WriteResourceReceipt(resourceReceiptPath, endpoint.Port, startHttp, false, false);
            return new(app, certificate, endpoint, startHttp, resourceReceiptPath);
        }
        catch
        {
            try { if (app is not null) await app.DisposeAsync(); }
            finally { certificate.Dispose(); }
            WriteResourceReceipt(resourceReceiptPath, endpoint.Port, false, false, true);
            throw;
        }
    }

    public HttpClient CreateClient(out CookieContainer cookies)
    {
        Assert.IsTrue(started);
        cookies = new CookieContainer();
        return new HttpClient(CreatePinnedHandler(cookies)) { BaseAddress = Endpoint, Timeout = TimeSpan.FromSeconds(20) };
    }

    internal HttpClientHandler CreatePinnedHandler(CookieContainer cookies)
    {
        Assert.IsTrue(started);
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, supplied, _, _) => supplied is not null
                && CryptographicOperations.FixedTimeEquals(supplied.RawData, certificate.RawData)
        };
        return handler;
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> work) => InScopeAsync(async services => { await work(services); return true; });

    public Task<Guid> InviteAsync(string email, string invitationProof) => InScopeAsync(services =>
        services.GetRequiredService<InvitationService>().IssueAsync(email, invitationProof,
            services.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(7), CancellationToken.None));

    public Task<string> RegistrationProofAsync(Guid pendingId) => InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var challenge = await db.EmailChallenges.AsNoTracking().Where(c => c.PendingRegistrationId == pendingId
            && c.RevokedAt == null && c.ConsumedAt == null).SingleAsync();
        return services.GetRequiredService<IEmailProofProtector>().Protect(challenge);
    });

    public static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/v1/auth/request-protection");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("requestToken").GetString()!;
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        object? body = null, string? resumeProof = null, bool csrf = true, string? origin = null)
    {
        var token = csrf ? await CsrfAsync(client) : null;
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        if (resumeProof is not null) request.Headers.Add("X-Registration-Proof", resumeProof);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.AreEqual(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Target> ValidateTargetAsync(bool requireB5AApproval, bool requireB5BApproval, bool requireB5CApproval)
    {
        // This check precedes connection, port probing, certificate creation and key-ring access.
        // Earlier-stage approval must never authorize new client integration writes.
        var approval = requireB5CApproval ? "DANGO_BACKEND_B5C_TEST_APPROVED"
            : requireB5BApproval ? "DANGO_BACKEND_B5B_TEST_APPROVED"
            : requireB5AApproval ? "DANGO_BACKEND_B5A_TEST_APPROVED" : "DANGO_BACKEND_B1_TEST_APPROVED";
        if (Environment.GetEnvironmentVariable(approval) != "1")
            Assert.Inconclusive(requireB5CApproval
                ? "B5C formal Hub and lifecycle writes require separate precise authorization."
                : requireB5BApproval
                ? "B5B client room writes require separate precise authorization."
                : requireB5AApproval
                ? "B5A client account writes require separate precise authorization."
                : "B1 fictional account and email writes require separate precise authorization.");
        var supplied = Environment.GetEnvironmentVariable("DANGO_BACKEND_TEST_CONNECTION");
        var directory = Environment.GetEnvironmentVariable("DANGO_BACKEND_TEST_DATA_DIRECTORY");
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(directory))
            Assert.Inconclusive("An explicitly authorized isolated PostgreSQL target is required.");
        var physical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory!));
        var approved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dango", "BackendDevelopment", "run20261008b0", "pgdata");
        Assert.AreEqual(Path.GetFullPath(approved), physical, true);
        for (DirectoryInfo? part = new(physical); part is not null; part = part.Parent)
            Assert.IsFalse(part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0);
        var builder = new NpgsqlConnectionStringBuilder(supplied)
        {
            IncludeErrorDetail = false,
            LogParameters = false,
            Timeout = 5,
            CommandTimeout = 15,
            Timezone = "UTC"
        };
        Assert.AreEqual("127.0.0.1", builder.Host);
        Assert.AreEqual("dango_backend_b0", builder.Database);
        if (requireB5AApproval || requireB5BApproval || requireB5CApproval)
        {
            Assert.AreEqual("dango_b0_owner", builder.Username);
            var statePath = Path.Combine(Path.GetDirectoryName(physical)!, "state.json");
            var stateFile = new FileInfo(statePath);
            Assert.IsTrue(stateFile.Exists && stateFile.Length is > 0 and <= 8192
                && (stateFile.Attributes & FileAttributes.ReparsePoint) == 0);
            using var state = JsonDocument.Parse(await File.ReadAllBytesAsync(statePath));
            var facts = state.RootElement;
            Assert.AreEqual("run20261008b0", facts.GetProperty("RunName").GetString());
            Assert.AreEqual("dango_backend_b0", facts.GetProperty("Database").GetString());
            Assert.AreEqual("dango_b0_owner", facts.GetProperty("Owner").GetString());
            Assert.AreEqual(physical, Path.TrimEndingDirectorySeparator(Path.GetFullPath(facts.GetProperty("DataDirectory").GetString()!)), true);
            Assert.AreEqual(1, facts.GetProperty("Version").GetInt32());
            Assert.AreEqual(facts.GetProperty("Port").GetInt32(), builder.Port);
        }
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_database(), current_setting('server_version_num')::integer, current_setting('data_directory'), current_setting('listen_addresses'), current_user", connection);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual("dango_backend_b0", reader.GetString(0));
            Assert.IsTrue(reader.GetInt32(1) is >= 180000 and < 190000);
            Assert.AreEqual(physical, Path.TrimEndingDirectorySeparator(Path.GetFullPath(reader.GetString(2))), true);
            if (requireB5AApproval || requireB5BApproval || requireB5CApproval)
            {
                Assert.AreEqual("127.0.0.1", reader.GetString(3));
                Assert.AreEqual("dango_b0_owner", reader.GetString(4));
            }
        }
        await using var db = new DangoDbContext(DangoDatabaseOptions.Create(builder.ConnectionString));
        Assert.IsFalse((await db.Database.GetPendingMigrationsAsync()).Any(), "The authorized runner must apply migrations explicitly before these gates.");
        return new(builder.ConnectionString, physical);
    }

    private static string FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("The test repository boundary is unavailable.");
    }

    private static int FindLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Dango fictional acceptance", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var privateMaterial = generated.Export(X509ContentType.Pkcs12);
        // Windows Schannel requires a native key container. DefaultKeySet is temporary and
        // X509Certificate2 disposal removes only this generated certificate's container.
        try { return X509CertificateLoader.LoadPkcs12(privateMaterial, null, X509KeyStorageFlags.DefaultKeySet); }
        finally { CryptographicOperations.ZeroMemory(privateMaterial); }
    }

    public async ValueTask DisposeAsync()
    {
        var stopped = !started;
        var disposed = false;
        try { if (started) await app.StopAsync(); stopped = true; }
        finally
        {
            try { await app.DisposeAsync(); disposed = true; }
            finally
            {
                certificate.Dispose();
                WriteResourceReceipt(resourceReceiptPath, Endpoint.Port, started, stopped, disposed);
            }
        }
    }

    private static string B5CResourcePath(string repository, int port)
    {
        var supplied = Environment.GetEnvironmentVariable("DANGO_BACKEND_B5C_RESOURCE_DIRECTORY");
        Assert.IsFalse(string.IsNullOrEmpty(supplied), "B5C needs the runner-owned ignored resource directory.");
        var path = Path.GetFullPath(supplied!);
        var approved = Path.GetFullPath(Path.Combine(repository, "artifacts", "backend-b5c", "postgres-tests"))
            + Path.DirectorySeparatorChar;
        Assert.IsTrue(path.StartsWith(approved, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path));
        for (DirectoryInfo? part = new(path); part is not null; part = part.Parent)
            Assert.IsFalse(part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0);
        return Path.Combine(path, $"fixture-{port}-{Guid.NewGuid():N}.json");
    }

    private static void WriteResourceReceipt(string? path, int port, bool started, bool stopped, bool disposed)
    {
        if (path is null) return;
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Version = 1, Port = port, ProcessId = Environment.ProcessId, Started = started,
            Stopped = stopped, Disposed = disposed
        }));
    }

    private sealed record Target(string ConnectionString, string DataDirectory);
}

internal sealed class MutableTestClock(DateTimeOffset initial) : TimeProvider
{
    private long utcTicks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref utcTicks, duration.Ticks);
}
