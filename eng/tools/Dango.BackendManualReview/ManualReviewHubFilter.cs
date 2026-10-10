using System.Security.Cryptography;
using System.Text.Json;
using Dango.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace Dango.BackendManualReview;

public sealed class ManualReviewHubFilter(ManualReviewBudget budget) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        RequireOwn(invocation.Context);
        var arguments = invocation.HubMethodArguments;
        var method = invocation.HubMethodName;
        if (method == "PersonalSnapshot" && arguments.Count == 0) return await next(invocation);
        if (method is "Attach" or "Heartbeat" && arguments is [RoomLeaseRequest request])
        {
            if (!budget.IsLease(request.LeaseId, request.InstanceId)) throw Reject();
            return await next(invocation);
        }
        if (method == "Snapshot" && arguments is [Guid roomId, RoomLeaseRequest snapshotRequest])
        {
            if (!budget.IsLease(snapshotRequest.LeaseId, snapshotRequest.InstanceId, roomId)) throw Reject();
            return await next(invocation);
        }
        Guid requestId;
        if (method is "TakeControl" or "Leave" && arguments is [RoomControlRequest control])
        {
            if (!budget.IsLease(control.LeaseId, control.InstanceId, control.ExpectedRoomId)) throw Reject();
            requestId = control.RequestId;
        }
        else if (method == "EndLease" && arguments is [RoomLeaseEndRequest end])
        {
            if (!budget.IsLease(end.LeaseId, end.InstanceId)) throw Reject();
            requestId = end.RequestId;
        }
        else throw Reject();
        if (requestId == Guid.Empty) throw Reject();
        using var gate = await budget.LockMutationAsync(invocation.Context.ConnectionAborted);
        RequireOwn(invocation.Context);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { method, arguments });
        try
        {
            try { budget.Reserve(ManualReviewOperation.Other, "room:" + requestId.ToString("D"), payload); }
            catch (InvalidOperationException error) when (error.Message.StartsWith("manual_review_", StringComparison.Ordinal))
            { throw new HubException(error.Message); }
            return await next(invocation);
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        RequireOwn(context.Context);
        await next(context);
    }

    private void RequireOwn(HubCallerContext context)
    {
        if (context.User is null || !budget.Owns(context.User)) { context.Abort(); throw Reject(); }
    }
    private static HubException Reject() => new("manual_review_scope_rejected");
}
