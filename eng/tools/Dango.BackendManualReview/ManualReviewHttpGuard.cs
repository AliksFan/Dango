using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Protocol;
using Dango.Server.Hosting;
using Microsoft.AspNetCore.Http;

namespace Dango.BackendManualReview;

public sealed class ManualReviewHttpGuard(RequestDelegate next, ManualReviewBudget budget)
{
    private const int MaximumRequestBytes = 8192;
    private const int MaximumResponseBytes = 65536;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false, MaxDepth = 16
    };

    public async Task InvokeAsync(HttpContext context)
    {
        byte[]? body = null;
        MemoryStream? requestCopy = null;
        var originalRequestBody = context.Request.Body;
        try
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;
            if (IsPublicRead(path, method, context)) { await next(context); return; }
            if (path is "/api/v1/room-hub" or "/api/v1/room-hub/negotiate")
            {
                RequireOwn(context);
                if (path.EndsWith("/negotiate", StringComparison.Ordinal) ? !HttpMethods.IsPost(method)
                    : !HttpMethods.IsGet(method) && !HttpMethods.IsPost(method) && !HttpMethods.IsDelete(method)) throw ManualReviewBudget.Reject();
                await next(context);
                return;
            }
            if (HttpMethods.IsGet(method))
            {
                ValidateRead(context, path);
                await next(context);
                return;
            }
            // All mutation reservations, execution and success-ID capture share this gate. A concurrent
            // request cannot spend an unbound room/lease or race the last quota before it is reserved.
            using var gate = await budget.LockMutationAsync(context.RequestAborted);
            body = await ReadBoundedAsync(context.Request.Body, MaximumRequestBytes, context.RequestAborted);
            requestCopy = new MemoryStream(body, writable: false);
            context.Request.Body = requestCopy;
            using var document = body.Length > 0 ? JsonDocument.Parse(body, new() { MaxDepth = 16, AllowDuplicateProperties = false }) : null;
            if (HttpMethods.IsPost(method) && path == "/api/v1/auth/login" && budget.Identity is null)
            {
                var login = Read<LoginRequest>(document?.RootElement);
                if (!budget.IsEmail(login.Email)) throw ManualReviewBudget.Reject();
                await budget.RecoverIdentityAsync(context.RequestAborted);
            }
            var plan = PlanMutation(context, path, document?.RootElement);
            var proof = context.Request.Headers["X-Registration-Proof"];
            if (proof.Count > 1 || proof.ToString().Length > 4096 || context.Request.Query.Count != 0) throw ManualReviewBudget.Reject();
            var prefix = Encoding.UTF8.GetBytes(method + "\n" + path + "\n" + proof + "\n");
            var hashInput = new byte[prefix.Length + body.Length];
            prefix.CopyTo(hashInput, 0); body.CopyTo(hashInput, prefix.Length);
            try { budget.Reserve(plan.Operation, plan.Key, hashInput); }
            finally { CryptographicOperations.ZeroMemory(hashInput); CryptographicOperations.ZeroMemory(prefix); }
            if (plan.Capture is null) { await next(context); return; }
            var originalResponseBody = context.Response.Body;
            using var capture = new LimitedMemoryStream(MaximumResponseBytes);
            context.Response.Body = capture;
            try
            {
                await next(context);
                if (context.Response.StatusCode is >= 200 and < 300)
                {
                    capture.Position = 0;
                    using var result = await JsonDocument.ParseAsync(capture,
                        new() { MaxDepth = 16, AllowDuplicateProperties = false }, CancellationToken.None);
                    // Once a successful response is available, the known commit must be captured
                    // even when the client disconnects. Only copying to that client uses its token.
                    await plan.Capture(result.RootElement, CancellationToken.None);
                }
                capture.Position = 0;
                await capture.CopyToAsync(originalResponseBody, context.RequestAborted);
            }
            finally
            {
                context.Response.Body = originalResponseBody;
                if (capture.TryGetBuffer(out var segment)) CryptographicOperations.ZeroMemory(segment.AsSpan());
            }
        }
        catch (InvalidOperationException error) when (error.Message.StartsWith("manual_review_", StringComparison.Ordinal))
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = error.Message == "manual_review_budget_exhausted" ? 429
                    : error.Message == "manual_review_request_conflict" ? 409 : 403;
                await context.Response.WriteAsJsonAsync(new { code = error.Message }, context.RequestAborted);
            }
        }
        catch (JsonException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { code = "manual_review_input_invalid" }, context.RequestAborted);
            }
        }
        finally
        {
            context.Request.Body = originalRequestBody;
            requestCopy?.Dispose();
            if (body is not null) CryptographicOperations.ZeroMemory(body);
        }
    }

    private static bool IsPublicRead(string path, string method, HttpContext context) => HttpMethods.IsGet(method)
        && (path is "/api/v1/auth/request-protection" or "/health/ready") && context.Request.Query.Count == 0;

    private void ValidateRead(HttpContext context, string path)
    {
        var parts = Parts(path);
        if (parts is ["api", "v1", "auth", "registrations", var pending, "status"])
        {
            if (!budget.IsRegistration(Id(pending)) || context.Request.Query.Count != 0) throw ManualReviewBudget.Reject();
            return;
        }
        RequireOwn(context);
        if (path is "/api/v1/me" or "/api/v1/me/pet" or "/api/v1/me/sessions" or "/api/v1/me/room/snapshot")
        {
            if (context.Request.Query.Count != 0) throw ManualReviewBudget.Reject();
            return;
        }
        if (path == "/api/v1/me/rooms")
        {
            if (context.Request.Query.Any(q => q.Key is not ("state" or "limit" or "after") || q.Value.Count != 1)
                || context.Request.Query.TryGetValue("after", out var after) && !budget.IsRoom(Id(after.ToString()))) throw ManualReviewBudget.Reject();
            return;
        }
        if (parts is ["api", "v1", "rooms", var room])
        {
            if (!budget.IsRoom(Id(room)) || context.Request.Query.Count != 0) throw ManualReviewBudget.Reject();
            return;
        }
        if (parts is ["api", "v1", "rooms", var snapshotRoom, "snapshot"])
        {
            if (context.Request.Query.Count != 2 || context.Request.Query["leaseId"].Count != 1 || context.Request.Query["instanceId"].Count != 1
                || !budget.IsLease(Id(context.Request.Query["leaseId"].ToString()), Id(context.Request.Query["instanceId"].ToString()), Id(snapshotRoom)))
                throw ManualReviewBudget.Reject();
            return;
        }
        throw ManualReviewBudget.Reject();
    }

    private Mutation PlanMutation(HttpContext context, string path, JsonElement? body)
    {
        var method = context.Request.Method;
        var parts = Parts(path);
        if (HttpMethods.IsPost(method) && path == "/api/v1/auth/registrations")
        {
            var request = Read<RegistrationStartRequest>(body);
            if (!budget.IsEmail(request.Email) || request.InvitationId != budget.InvitationId) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.RegistrationStart, "registration:start:" + Id(request.RequestKey),
                (result, _) => { budget.CaptureRegistration(Id(result, "id")); return Task.CompletedTask; });
        }
        if (HttpMethods.IsPost(method) && path == "/api/v1/auth/email-verifications/confirm")
        {
            var request = Read<EmailProofRequest>(body);
            if (!budget.IsConfirmation(request.Proof)) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.Other, "registration:confirm", null);
        }
        if (HttpMethods.IsPost(method) && parts is ["api", "v1", "auth", "registrations", var pending, "complete"])
        {
            var request = Read<RegistrationCompleteRequest>(body);
            if (!budget.IsRegistration(Id(pending))) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.RegistrationComplete, "registration:complete:" + Id(request.CompletionKey),
                (result, ct) => budget.CaptureIdentityAsync(new(Id(result, "userId"), Id(result, "petId"), Id(result, "roomId")), ct));
        }
        if (HttpMethods.IsPost(method) && path == "/api/v1/auth/login")
        {
            var request = Read<LoginRequest>(body);
            if (!budget.IsEmail(request.Email) || budget.Identity is null) throw ManualReviewBudget.Reject();
            // Formal login has no idempotency key. Identical requests can create distinct sessions.
            return new(ManualReviewOperation.Login, "login:" + Guid.NewGuid(),
                (result, _) => { budget.CaptureSession(Id(result, "userId"), Id(result, "sessionId")); return Task.CompletedTask; });
        }
        RequireOwn(context);
        if (HttpMethods.IsPost(method) && path == "/api/v1/auth/logout" && body is null)
            return new(ManualReviewOperation.Other, "logout:" + Guid.NewGuid(), null);
        if (HttpMethods.IsPatch(method) && path == "/api/v1/me/pet/configuration")
        {
            var request = Read<PetConfigurationUpdate>(body);
            if (request.ExpectedVersion <= 0) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.Configuration, "configuration:" + request.ExpectedVersion, null);
        }
        if (HttpMethods.IsPost(method) && path == "/api/v1/rooms")
        {
            var request = Read<RoomCreateRequest>(body);
            if (request.Kind is not ("privateShared" or "publicShared")) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.RoomCreate, RoomKey(request.RequestId),
                (result, _) => { budget.CaptureRoom(Id(result, "roomId")); return Task.CompletedTask; });
        }
        if (HttpMethods.IsPost(method) && path == "/api/v1/rooms/resolve")
        {
            var request = Read<RoomCodeResolveRequest>(body);
            if (!budget.IsCode(request.Code)) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.Other, "resolve:" + Guid.NewGuid(),
                (result, _) => { if (!budget.IsRoom(Id(result, "roomId"))) throw ManualReviewBudget.Reject(); return Task.CompletedTask; });
        }
        if (parts is ["api", "v1", "rooms", var roomText, ..])
        {
            var room = Id(roomText);
            if (!budget.IsRoom(room)) throw ManualReviewBudget.Reject();
            if (HttpMethods.IsPost(method) && parts is [_, _, _, _, "enter"])
            {
                var request = Read<RoomEnterRequest>(body);
                Id(request.InstanceId);
                if (request.ExpectedRoomId is { } expected && !budget.IsRoom(expected) || request.Code is not null && !budget.IsCode(request.Code))
                    throw ManualReviewBudget.Reject();
                return new(ManualReviewOperation.Enter, RoomKey(request.RequestId), (result, _) =>
                {
                    if (Id(result, "roomId") != room) throw ManualReviewBudget.Reject();
                    budget.CaptureLease(Id(result, "petId"), room, Id(result, "leaseId"), request.InstanceId);
                    return Task.CompletedTask;
                });
            }
            if (HttpMethods.IsPost(method) && parts is [_, _, _, _, "code", "reset"])
            {
                var request = Read<RoomCodeResetRequest>(body);
                return new(ManualReviewOperation.CodeReset, RoomKey(request.RequestId), (result, _) =>
                {
                    if (Id(result, "roomId") != room) throw ManualReviewBudget.Reject();
                    budget.CaptureCode(room, result.GetProperty("code").GetString() ?? string.Empty);
                    return Task.CompletedTask;
                });
            }
            if (HttpMethods.IsPatch(method) && parts.Length == 4)
                return new(ManualReviewOperation.Other, RoomKey(Read<RoomUpdateRequest>(body).RequestId), null);
            if (HttpMethods.IsPost(method) && parts is [_, _, _, _, "dissolve"])
                return new(ManualReviewOperation.Other, RoomKey(Read<RoomDissolveRequest>(body).RequestId), null);
            if (HttpMethods.IsPost(method) && parts is [_, _, _, _, "restore"])
                return new(ManualReviewOperation.Other, RoomKey(Read<RoomRestoreRequest>(body).RequestId), null);
        }
        if (HttpMethods.IsPost(method) && (path is "/api/v1/me/room/control" or "/api/v1/me/room/leave"))
        {
            var request = Read<RoomControlRequest>(body);
            if (!budget.IsLease(request.LeaseId, request.InstanceId, request.ExpectedRoomId)) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.Other, RoomKey(request.RequestId), null);
        }
        if (HttpMethods.IsPost(method) && path == "/api/v1/me/room/lease/end")
        {
            var request = Read<RoomLeaseEndRequest>(body);
            if (!budget.IsLease(request.LeaseId, request.InstanceId)) throw ManualReviewBudget.Reject();
            return new(ManualReviewOperation.Other, RoomKey(request.RequestId), null);
        }
        throw ManualReviewBudget.Reject();
    }

    private void RequireOwn(HttpContext context) { if (!budget.Owns(context.User)) throw ManualReviewBudget.Reject(); }
    private static string[] Parts(string path) => path.StartsWith("/", StringComparison.Ordinal)
        && !path.EndsWith("/", StringComparison.Ordinal) ? path[1..].Split('/') : [];
    private static Guid Id(string text) => Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty ? id : throw ManualReviewBudget.Reject();
    private static Guid Id(Guid id) => id != Guid.Empty ? id : throw ManualReviewBudget.Reject();
    private static Guid Id(JsonElement result, string name) => result.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String ? Id(property.GetString()!) : throw ManualReviewBudget.Reject();
    private static string RoomKey(Guid id) => "room:" + Id(id).ToString("D");
    private static T Read<T>(JsonElement? body) => body is { ValueKind: JsonValueKind.Object } value
        ? value.Deserialize<T>(Json) ?? throw ManualReviewBudget.Reject() : throw ManualReviewBudget.Reject();
    private static async Task<byte[]> ReadBoundedAsync(Stream source, int limit, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0) return output.ToArray();
                if (output.Length + read > limit) throw ManualReviewBudget.Reject();
                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            if (output.TryGetBuffer(out var segment)) CryptographicOperations.ZeroMemory(segment.AsSpan());
        }
    }
    private sealed record Mutation(ManualReviewOperation Operation, string Key, Func<JsonElement, CancellationToken, Task>? Capture);
    private sealed class LimitedMemoryStream(int limit) : MemoryStream
    {
        private void Check(int size) { if (Position + size > limit) throw ManualReviewBudget.Reject(); }
        public override long Position { get => base.Position; set { if (value < 0 || value > limit) throw ManualReviewBudget.Reject(); base.Position = value; } }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void SetLength(long value) { if (value > limit || value < 0) throw ManualReviewBudget.Reject(); base.SetLength(value); }
        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, SeekOrigin.End => Length + offset, _ => -1 };
            if (target < 0 || target > limit) throw ManualReviewBudget.Reject();
            return base.Seek(offset, origin);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) { Check(count); return base.WriteAsync(buffer, offset, count, ct); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { Check(buffer.Length); return base.WriteAsync(buffer, ct); }
    }
}
