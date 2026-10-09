using System.Security.Claims;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Protocol;
using Dango.Server.Pets;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace Dango.Server.Hosting;

public sealed record RegistrationStartRequest(string Email, Guid InvitationId, string InvitationProof, Guid RequestKey)
{ public override string ToString() => "RegistrationStartRequest [redacted]"; }
public sealed record RegistrationCompleteRequest(Guid CompletionKey, string Password, JsonElement Appearance)
{ public override string ToString() => "RegistrationCompleteRequest [redacted]"; }
public sealed record EmailProofRequest(string Proof)
{ public override string ToString() => "EmailProofRequest [redacted]"; }
public sealed record LoginRequest(string Email, string Password, bool Remembered)
{ public override string ToString() => "LoginRequest [redacted]"; }
public sealed record ResetRequest(string Email);
public sealed record ResetCompleteRequest(string Proof, string Password)
{ public override string ToString() => "ResetCompleteRequest [redacted]"; }
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword)
{ public override string ToString() => "ChangePasswordRequest [redacted]"; }

public static class AccountEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/auth/request-protection", (HttpContext ctx, IAntiforgery protection) =>
            Results.Ok(new { requestToken = protection.GetAndStoreTokens(ctx).RequestToken }));
        api.MapPost("/auth/registrations", async (RegistrationStartRequest request, HttpContext ctx, RegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.StartAsync(request.Email, request.InvitationId, request.InvitationProof,
                request.RequestKey, FormalHost.ResumeProof(ctx), ct)));
        api.MapGet("/auth/registrations/{id:guid}/status", async (Guid id, HttpContext ctx, RegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.StatusAsync(id, FormalHost.ResumeProof(ctx), ct)));
        api.MapPost("/auth/registrations/{id:guid}/resend", async (Guid id, HttpContext ctx, RegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.ResendAsync(id, FormalHost.ResumeProof(ctx), ct)));
        api.MapPost("/auth/email-verifications/confirm", async (EmailProofRequest request, RegistrationService service, CancellationToken ct) =>
        {
            await service.ConfirmAsync(request.Proof, ct);
            return Results.Ok(new { status = "verified" });
        });
        api.MapPost("/auth/registrations/{id:guid}/complete", async (Guid id, RegistrationCompleteRequest request,
            HttpContext ctx, RegistrationService service, CancellationToken ct) =>
                Results.Ok(await service.CompleteAsync(id, FormalHost.ResumeProof(ctx), request.CompletionKey, request.Password, request.Appearance, ct)));
        api.MapPost("/auth/login", async (LoginRequest request, HttpContext ctx, SessionService service, CancellationToken ct) =>
        {
            var login = await service.LoginAsync(request.Email, request.Password, request.Remembered, ct);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, login.UserId.ToString("D")), new Claim(FormalHost.SessionClaim, login.SessionId.ToString("D"))], FormalHost.Scheme));
            await ctx.SignInAsync(FormalHost.Scheme, principal, new AuthenticationProperties
            {
                IsPersistent = login.Remembered,
                ExpiresUtc = login.ExpiresAt,
                AllowRefresh = false
            });
            return Results.Ok(new { login.UserId, login.SessionId, login.ExpiresAt });
        });
        api.MapPost("/auth/logout", async (HttpContext ctx, SessionService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), ct);
            await ctx.SignOutAsync(FormalHost.Scheme);
            return Results.Ok(new { status = "signed_out" });
        }).RequireAuthorization();
        api.MapGet("/me", (HttpContext ctx) => Results.Ok(new { userId = FormalHost.UserId(ctx) })).RequireAuthorization();
        api.MapGet("/me/pet", async (HttpContext ctx, PetConfigurationService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), ct))).RequireAuthorization();
        api.MapPatch("/me/pet/configuration", async (PetConfigurationUpdate update, HttpContext ctx, PetConfigurationService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), update, ct))).RequireAuthorization();
        api.MapGet("/me/sessions", async (HttpContext ctx, SessionService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(FormalHost.UserId(ctx), ct))).RequireAuthorization();
        api.MapDelete("/me/sessions/{id:guid}", async (Guid id, HttpContext ctx, SessionService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(FormalHost.UserId(ctx), id, ct);
            if (id == FormalHost.SessionId(ctx)) await ctx.SignOutAsync(FormalHost.Scheme);
            return Results.NoContent();
        }).RequireAuthorization();
        api.MapPost("/auth/password-reset/request", async (ResetRequest request, CredentialService service, CancellationToken ct) =>
        {
            await service.RequestPasswordResetAsync(request.Email, ct);
            return Results.Ok(new { status = "accepted" });
        });
        api.MapPost("/auth/password-reset/complete", async (ResetCompleteRequest request, CredentialService service, CancellationToken ct) =>
        {
            await service.CompletePasswordResetAsync(request.Proof, request.Password, ct);
            return Results.Ok(new { status = "password_updated" });
        });
        api.MapPost("/auth/password/change", async (ChangePasswordRequest request, HttpContext ctx, CredentialService service, CancellationToken ct) =>
        {
            await service.ChangePasswordAsync(FormalHost.UserId(ctx), request.CurrentPassword, request.NewPassword, ct);
            await ctx.SignOutAsync(FormalHost.Scheme);
            return Results.Ok(new { status = "password_updated" });
        }).RequireAuthorization();
        app.MapGet("/auth/email-confirm", (HttpContext ctx, IAntiforgery protection) =>
            ConfirmationPage.Render(ctx, protection, reset: false));
        app.MapGet("/auth/password-reset", (HttpContext ctx, IAntiforgery protection) =>
            ConfirmationPage.Render(ctx, protection, reset: true));
    }
}
