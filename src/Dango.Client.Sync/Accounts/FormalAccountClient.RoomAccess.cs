using System.Net;
using System.Text.Json;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;
using Microsoft.AspNetCore.Http.Connections;

namespace Dango.Client.Sync.Accounts;

public sealed partial class FormalAccountClient
{
    public IRoomAccessClient CreateRoomAccessClient(AccountIdentity identity) => CreateRoomAccessBinding(identity, null, null);

    // The only certificate/transport override seam is explicitly test-only. Production always uses system TLS validation.
    public IRoomAccessClient CreateRoomAccessClientForTesting(AccountIdentity identity,
        Func<HttpMessageHandler, HttpMessageHandler> hubHandlerFactory, HttpTransportType transports = HttpTransportType.LongPolling) =>
        CreateRoomAccessBinding(identity, hubHandlerFactory ?? throw new ArgumentNullException(nameof(hubHandlerFactory)), transports);

    private IRoomAccessClient CreateRoomAccessBinding(AccountIdentity identity,
        Func<HttpMessageHandler, HttpMessageHandler>? handlerFactory, HttpTransportType? transports)
    {
        ThrowIfDisposed();
        if (identity is null || identity.UserId == Guid.Empty || identity.PetId == Guid.Empty || CurrentIdentity != identity)
            throw new RoomAccessClientException(RoomAccessFailure.Authentication, "room_binding_changed");
        return new BoundRoomAccessClient(this, identity, sessionGeneration, handlerFactory, transports);
    }

    private sealed class BoundRoomAccessClient(FormalAccountClient owner, AccountIdentity identity, int generation,
        Func<HttpMessageHandler, HttpMessageHandler>? handlerFactory, HttpTransportType? transports) : IRoomAccessClient
    {
        public Uri Origin => owner.Origin;
        public Guid UserId => identity.UserId;
        public Guid PetId => identity.PetId;
        public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct) => ExecuteAsync(async token =>
            RoomAccessValidation.Personal(await RequestAsync(HttpMethod.Get, "api/v1/me/room/snapshot", null, token), UserId, PetId), ct);
        public Task<SharedRoomSnapshot> SnapshotAsync(Guid roomId, RoomLeaseRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RoomAccessValidation.Id(roomId); RoomAccessValidation.Lease(request);
            return RoomAccessValidation.Snapshot(await RequestAsync(HttpMethod.Get,
                $"api/v1/rooms/{roomId:D}/snapshot?leaseId={request.LeaseId:D}&instanceId={request.InstanceId:D}", null, token), roomId);
        }, ct);
        public Task<RoomAccessResult> EnterAsync(Guid roomId, RoomEnterRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RoomAccessValidation.Id(roomId); RoomAccessValidation.Enter(request);
            return RoomAccessValidation.Result(await RequestAsync(HttpMethod.Post, $"api/v1/rooms/{roomId:D}/enter", request, token), PetId, "entered", roomId,
                expectedAssignmentVersion: request.ExpectedAssignmentVersion);
        }, ct);
        public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RoomAccessValidation.Control(request);
            return RoomAccessValidation.Result(await RequestAsync(HttpMethod.Post, "api/v1/me/room/control", request, token), PetId, "control", request.ExpectedRoomId, request.LeaseId,
                request.ExpectedAssignmentVersion, request.ExpectedControlGeneration);
        }, ct);
        public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RoomAccessValidation.Control(request);
            return RoomAccessValidation.Result(await RequestAsync(HttpMethod.Post, "api/v1/me/room/leave", request, token), PetId, "left",
                expectedAssignmentVersion: request.ExpectedAssignmentVersion, expectedControlGeneration: request.ExpectedControlGeneration);
        }, ct);
        public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RoomAccessValidation.End(request);
            return RoomAccessValidation.Result(await RequestAsync(HttpMethod.Post, "api/v1/me/room/lease/end", request, token), PetId, "lease_ended");
        }, ct);
        public Task<RoomDoorView> ResolveAsync(RoomCodeResolveRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            if (request is null) throw RoomAccessValidation.Reject("room_request_invalid"); RoomAccessValidation.Code(request.Code);
            return RoomAccessValidation.Door(await RequestAsync(HttpMethod.Post, "api/v1/rooms/resolve", request, token));
        }, ct);
        public Task<RoomCodeResult> ResetCodeAsync(Guid roomId, RoomCodeResetRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RoomAccessValidation.Id(roomId); RoomAccessValidation.Reset(request);
            return RoomAccessValidation.CodeResult(await RequestAsync(HttpMethod.Post, $"api/v1/rooms/{roomId:D}/code/reset", request, token), roomId, request.ExpectedVersion);
        }, ct);
        private Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct) =>
            owner.RequestAsync(method, path, body, null, ct, maximumSuccessBytes: RoomAccessValidation.MaximumBytes,
                additionalErrorCodes: RoomAccessValidation.Errors);
        private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
        {
            try
            {
                return await owner.LockedAsync(async token =>
                { Check(token); var value = await operation(token); Check(token); return value; }, ct);
            }
            catch (AccountClientException error) { throw RoomAccessValidation.AccountError(error); }
        }
        private void Check(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); owner.ThrowIfDisposed();
            if (generation != owner.sessionGeneration || owner.CurrentIdentity != identity)
                throw new RoomAccessClientException(RoomAccessFailure.Authentication, "room_binding_changed");
        }
        public IFormalRoomConnection CreateConnection()
        {
            Check(CancellationToken.None);
            return new FormalRoomTransport(new(Origin, owner.Cookies, UserId, PetId, Check, owner.lifetime.Token, SendHubAsync, RejectAuthenticationAsync), handlerFactory, transports);
        }
        private async Task RejectAuthenticationAsync()
        {
            try
            {
                await owner.LockedAsync(async token =>
                { Check(token); owner.ClearMemory(); await owner.DeleteSecretAsync(AccountSecretSlots.Session, CancellationToken.None); return true; }, CancellationToken.None);
            }
            catch (Exception error) when (error is AccountClientException or RoomAccessClientException or ObjectDisposedException or OperationCanceledException)
            { /* The invalidated original binding cannot clear a later session. Memory is cleared before storage removal. */ }
        }
        // Holding the owner gate across Start/Invoke would deadlock the SignalR send path.
        // Only each unsafe HTTP CSRF+send pair owns it. Long polls never hold this gate.
        private async Task<HttpResponseMessage> SendHubAsync(HttpRequestMessage request,
            Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                Check(ct); var response = await send(ct);
                try { Check(ct); return response; } catch { response.Dispose(); throw; }
            }
            try
            {
                return await owner.LockedAsync(async token =>
                {
                    Check(token);
                    var json = await owner.RequestAsync(HttpMethod.Get, "api/v1/auth/request-protection", null, null, token);
                    Fields(json, "requestToken"); var proof = StringValue(json, "requestToken");
                    if (proof.Length is < 16 or > 4096 || proof.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                        throw AccountInputs.InvalidResponse();
                    request.Headers.Remove("X-CSRF-TOKEN"); request.Headers.Add("X-CSRF-TOKEN", proof);
                    Check(token); var response = await send(token);
                    try { Check(token); return response; } catch { response.Dispose(); throw; }
                }, ct);
            }
            catch (AccountClientException error) { throw RoomAccessValidation.AccountError(error); }
        }
    }
}
