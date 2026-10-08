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

    private FormalTestFixture(WebApplication app, X509Certificate2 certificate, Uri endpoint, bool started)
    {
        this.app = app;
        this.certificate = certificate;
        this.started = started;
        Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    public static async Task<FormalTestFixture> CreateAsync(bool startHttp = false, IRegistrationCheckpoint? checkpoint = null,
        TimeProvider? clock = null, IEmailSender? sender = null, bool enableEmailWorker = false,
        AccountEnvironment? environment = null)
    {
        var target = await ValidateTargetAsync();
        var epochText = Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_RECOVERY_EPOCH");
        Assert.IsTrue(Guid.TryParse(epochText, out var epoch) && epoch != Guid.Empty,
            "The authorized runner must supply the isolated B1 recovery epoch.");
        var keyDirectory = Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_KEY_DIRECTORY");
        Assert.IsFalse(string.IsNullOrEmpty(keyDirectory));
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(target.DataDirectory)!, "b1tests-keys"), Path.GetFullPath(keyDirectory!), true);
        var endpoint = new Uri($"https://127.0.0.1:{FindLoopbackPort()}/");
        var repository = FindRepository();
        Assert.IsTrue(FormalServerOptions.TryCreate("dango_backend_b0", target.ConnectionString, keyDirectory!,
            endpoint.AbsoluteUri, repository, out var options, out _));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Dango fictional acceptance", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var app = FormalHost.Create(options!, environment ?? new AccountEnvironment(epoch), certificate, services =>
        {
            if (checkpoint is not null) services.Replace(ServiceDescriptor.Singleton(checkpoint));
            if (clock is not null) services.Replace(ServiceDescriptor.Singleton(clock));
            if (sender is not null) services.Replace(ServiceDescriptor.Singleton(sender));
        }, enableEmailWorker);
        try
        {
            if (startHttp) await app.StartAsync();
            return new(app, certificate, endpoint, startHttp);
        }
        catch
        {
            await app.DisposeAsync();
            certificate.Dispose();
            throw;
        }
    }

    public HttpClient CreateClient(out CookieContainer cookies)
    {
        Assert.IsTrue(started);
        cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, supplied, _, _) => supplied is not null
                && CryptographicOperations.FixedTimeEquals(supplied.RawData, certificate.RawData)
        };
        return new HttpClient(handler) { BaseAddress = Endpoint, Timeout = TimeSpan.FromSeconds(20) };
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

    private static async Task<Target> ValidateTargetAsync()
    {
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_TEST_APPROVED") != "1")
            Assert.Inconclusive("B1 fictional account and email writes require separate precise authorization.");
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
            IncludeErrorDetail = false, LogParameters = false, Timeout = 5, CommandTimeout = 15, Timezone = "UTC"
        };
        Assert.AreEqual("127.0.0.1", builder.Host);
        Assert.AreEqual("dango_backend_b0", builder.Database);
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_database(), current_setting('server_version_num')::integer, current_setting('data_directory')", connection);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual("dango_backend_b0", reader.GetString(0));
            Assert.IsTrue(reader.GetInt32(1) is >= 180000 and < 190000);
            Assert.AreEqual(physical, Path.TrimEndingDirectorySeparator(Path.GetFullPath(reader.GetString(2))), true);
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

    public async ValueTask DisposeAsync()
    {
        if (started) await app.StopAsync();
        await app.DisposeAsync();
        certificate.Dispose();
    }

    private sealed record Target(string ConnectionString, string DataDirectory);
}

internal sealed class MutableTestClock(DateTimeOffset initial) : TimeProvider
{
    private long utcTicks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref utcTicks, duration.Ticks);
}
