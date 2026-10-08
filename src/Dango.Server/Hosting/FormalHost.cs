using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Email;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Hosting;

public static class FormalHost
{
    public const string Scheme = "DangoFormal";
    public const string SessionClaim = "dango_session";

    public static WebApplication Create(FormalServerOptions options, AccountEnvironment environment,
        X509Certificate2 certificate, Action<IServiceCollection>? configure = null, bool enableEmailWorker = true)
    {
        if (environment.RecoveryEpoch == Guid.Empty) throw new InvalidOperationException("recovery_epoch_required");
        if (!FormalServerOptions.TryCreate(options.DatabaseName, options.ConnectionString, options.KeyDirectory,
            options.Endpoint.AbsoluteUri, FormalDeploymentPaths.ProtectedRoot(), out _, out var configurationError))
            throw new InvalidOperationException(configurationError);
        FormalKeyDirectory.Protect(options.KeyDirectory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, options.Endpoint.Port, listen => listen.UseHttps(certificate));
            server.Limits.MaxConcurrentConnections = 128;
            server.Limits.MaxRequestBodySize = 8192;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(environment);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped(_ => new DangoDbContext(DangoDatabaseOptions.Create(options.ConnectionString)));
        builder.Services.AddIdentityCore<DangoUser>(identity =>
        {
            identity.User.RequireUniqueEmail = true;
            identity.User.AllowedUserNameCharacters = string.Empty;
            identity.Password.RequiredLength = 12;
            identity.Password.RequireDigit = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireLowercase = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Lockout.AllowedForNewUsers = true;
            identity.Lockout.MaxFailedAccessAttempts = 5;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<DangoDbContext>().AddDefaultTokenProviders();
        var protection = builder.Services.AddDataProtection()
            .SetApplicationName($"DangoFormal:{environment.RecoveryEpoch:D}")
            .PersistKeysToFileSystem(new DirectoryInfo(options.KeyDirectory));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        else throw new PlatformNotSupportedException("formal_key_protection_not_configured");
        builder.Services.AddSingleton<IEmailProofProtector, EmailProofProtector>();
        builder.Services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        builder.Services.AddSingleton<EmailDispatcher>();
        if (enableEmailWorker) builder.Services.AddHostedService<EmailWorker>();
        builder.Services.AddSingleton<IRegistrationCheckpoint, RegistrationCheckpoint>();
        builder.Services.AddScoped<ChallengeIssuer>();
        builder.Services.AddScoped<InvitationService>();
        builder.Services.AddScoped<RegistrationService>();
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<CredentialService>();
        builder.Services.AddAntiforgery(csrf =>
        {
            csrf.HeaderName = "X-CSRF-TOKEN";
            csrf.Cookie.Name = "__Host-Dango.Csrf";
            csrf.Cookie.Path = "/";
            csrf.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            csrf.Cookie.HttpOnly = true;
            csrf.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddAuthentication(Scheme).AddCookie(Scheme, cookie =>
        {
            cookie.Cookie.Name = "__Host-Dango.Auth";
            cookie.Cookie.Path = "/";
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.SlidingExpiration = false;
            cookie.Events.OnValidatePrincipal = async validation =>
            {
                if (!Guid.TryParse(validation.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                    || !Guid.TryParse(validation.Principal?.FindFirstValue(SessionClaim), out var sessionId)
                    || !await validation.HttpContext.RequestServices.GetRequiredService<SessionService>()
                        .ValidateAsync(userId, sessionId, validation.HttpContext.RequestAborted))
                {
                    validation.RejectPrincipal();
                    validation.HttpContext.Response.Cookies.Delete(cookie.Cookie.Name, new CookieOptions { Path = "/", Secure = true });
                }
            };
            cookie.Events.OnRedirectToLogin = ctx => ErrorAsync(ctx.HttpContext, "authentication_required", 401);
            cookie.Events.OnRedirectToAccessDenied = ctx => ErrorAsync(ctx.HttpContext, "access_denied", 403);
        });
        builder.Services.AddAuthorization();
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNameCaseInsensitive = false;
            json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            json.SerializerOptions.AllowDuplicateProperties = false;
            json.SerializerOptions.RespectNullableAnnotations = true;
            json.SerializerOptions.RespectRequiredConstructorParameters = true;
            json.SerializerOptions.MaxDepth = 16;
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        builder.Services.Configure<RouteHandlerOptions>(routes => routes.ThrowOnBadRequest = true);
        builder.Services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = 429;
            limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                    new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limits.OnRejected = (ctx, _) => new ValueTask(ErrorAsync(ctx.HttpContext, "request_rate_limited", 429));
        });
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            try { await next(); }
            catch (AccountFault fault) { await ErrorAsync(context, fault.Code, fault.Status); }
            catch (Exception ex) when (ex is BadHttpRequestException or JsonException)
            { await ErrorAsync(context, "request_invalid", 400); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            { await ErrorAsync(context, "write_conflict", 409); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { await ErrorAsync(context, "service_unavailable", 503); }
        });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin;
            if (origin.Count > 0 && (origin.Count != 1 || !string.Equals(origin[0], options.Endpoint.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal)))
                throw new AccountFault("origin_invalid", 403);
            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
                || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { throw new AccountFault("request_protection_invalid", 403); }
            }
            await next();
        });
        app.UseAuthorization();
        app.MapGet("/health/ready", async (DangoDbContext db, CancellationToken ct) =>
            (await db.Database.GetPendingMigrationsAsync(ct)).Any()
                ? Results.Json(new { code = "database_migration_required" }, statusCode: 503)
                : Results.Ok(new { status = "ready" }));
        AccountEndpoints.Map(app);
        // Explicitly no prototype /room Hub or pipe admission exists on this host.
        return app;
    }

    internal static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    internal static Guid SessionId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(SessionClaim)!);
    internal static string ResumeProof(HttpContext context) => context.Request.Headers["X-Registration-Proof"].Count == 1
        ? context.Request.Headers["X-Registration-Proof"].ToString() : string.Empty;
    private static Task ErrorAsync(HttpContext context, string code, int status)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { code, correlationId = context.TraceIdentifier });
    }
}
