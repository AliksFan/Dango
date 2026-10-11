namespace Dango.AccountSyncReview.Tests;

// Independent acceptance of the emitted trace. Missing evidence is never inferred.
internal static class AccountSyncB1c2LifecycleContract
{
    internal static void Verify(IReadOnlyList<AccountSyncB1c2Request> requests, IReadOnlyList<AccountSyncB1c2Phase> phases,
        IReadOnlyList<AccountSyncB1c2LifecycleEvent> events, IReadOnlyList<AccountSyncB1c2LogoutWindow> windows,
        long frequency, bool observersSettled)
    {
        if (!observersSettled || frequency <= 0 || events.Count is < 1 or > 4096 || requests.Count is < 1 or > 128 || windows.Count != 2) throw Reject();
        var kinds = new HashSet<string>(["RequestRegistered", "SendBefore", "SendAfter", "HeadersReceived", "BodyValidated", "CallerCancelled", "FixtureCancelled", "BodyDeadlineCancelled", "RequestCanceledTokenMatched", "RequestTerminal", "OperationStarted", "OperationCallerCancelled", "OperationTerminal", "CoordinatorBound", "CoordinatorStopped", "Phase", "WindowOpened", "UiaLogoutInvoked", "UiaLogoutReturned", "WindowClosed"], StringComparer.Ordinal);
        long previousTick = 0;
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Sequence != i + 1 || e.Tick < previousTick || !kinds.Contains(e.Kind)) throw Reject(); previousTick = e.Tick;
            if (e.RequestSequence is { } request && (request < 1 || request > requests.Count)) throw Reject();
            if (e.Kind is "RequestTerminal" or "OperationTerminal") { if (e.Value is not ("RanToCompletion" or "Canceled" or "Faulted")) throw Reject(); }
            else if (e.Kind == "RequestCanceledTokenMatched") { if (e.Value is not ("true" or "false")) throw Reject(); }
            else if (e.Kind == "CoordinatorStopped") { if (e.Value is not ("StoppedWithoutFailure" or "StoppedWithFailure")) throw Reject(); }
            else if (e.Kind == "Phase") { if (e.Value is null || !phases.Any(p => p.Phase == e.Value)) throw Reject(); }
            else if (e.Value is not null) throw Reject();
        }
        foreach (var group in events.Where(e => e.Kind == "OperationStarted").GroupBy(e => e.OperationId))
        {
            var start = group.Single();
            if (start.OperationId is not > 0 || start.CoordinatorId is not > 0 || start.RequestSequence is not null) throw Reject();
            var bound = events.Single(e => e.Kind == "CoordinatorBound" && e.CoordinatorId == start.CoordinatorId);
            var terminal = events.Single(e => e.Kind == "OperationTerminal" && e.OperationId == start.OperationId);
            if (bound.Sequence >= start.Sequence || terminal.Sequence <= start.Sequence || terminal.CoordinatorId != start.CoordinatorId || terminal.Value == "Faulted") throw Reject();
        }
        foreach (var e in events.Where(e => e.OperationId is not null))
        {
            var start = events.Single(x => x.Kind == "OperationStarted" && x.OperationId == e.OperationId);
            if (e.CoordinatorId != start.CoordinatorId || e.Sequence < start.Sequence) throw Reject();
        }
        long? priorAfter = null;
        foreach (var before in events.Where(e => e.Kind == "SendBefore"))
        {
            var after = events.Single(e => e.Kind == "SendAfter" && e.RequestSequence == before.RequestSequence);
            if (after.Sequence <= before.Sequence || priorAfter is { } prior && (decimal)before.Tick - prior < (decimal)frequency * 600 / 1000) throw Reject();
            priorAfter = after.Tick;
        }
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i]; var name = i == 0 ? "late-get" : "late-patch";
            if (w.WindowId != i + 1 || w.Case != name || !w.Verified || w.UserId == Guid.Empty || w.PetId == Guid.Empty || w.SessionId == Guid.Empty || w.CoordinatorId < 1) throw Reject();
            var opened = events.Single(e => e.Kind == "WindowOpened" && e.WindowId == w.WindowId);
            var invoked = events.Single(e => e.Kind == "UiaLogoutInvoked" && e.WindowId == w.WindowId);
            var returned = events.Single(e => e.Kind == "UiaLogoutReturned" && e.WindowId == w.WindowId);
            var closed = events.Single(e => e.Kind == "WindowClosed" && e.WindowId == w.WindowId);
            var converged = events.Single(e => e.Kind == "Phase" && e.Value == name + ":b-save-converged");
            var save = requests.Single(r => r.Sequence == w.SaveRequestSequence);
            var saveRegistered = events.Single(e => e.Kind == "RequestRegistered" && e.RequestSequence == save.Sequence);
            var logout = requests.Single(r => r.Sequence == w.LogoutRequestSequence);
            var logoutTerminal = events.Single(e => e.Kind == "RequestTerminal" && e.RequestSequence == logout.Sequence);
            if (converged.Sequence >= opened.Sequence || opened.Sequence >= invoked.Sequence || invoked.Sequence >= returned.Sequence || returned.Sequence >= closed.Sequence
                || logoutTerminal.Sequence >= closed.Sequence || logoutTerminal.Value != "RanToCompletion" || save.Sequence >= logout.Sequence
                || save.ClientId != w.ClientId || save.Actor != "b" || save.Role != "desktop" || save.Method != "PATCH" || save.Path != "/api/v1/me/pet/configuration" || save.ResponseStatus != 200
                || logout.ClientId != w.ClientId || logout.Actor != "b" || logout.Role != "desktop" || logout.Method != "POST" || logout.Path != "/api/v1/auth/logout" || logout.ResponseStatus != 200
                || save.SessionId != w.SessionId || logout.SessionId != w.SessionId || save.UserId != w.UserId || logout.UserId != w.UserId || save.PetId != w.PetId || logout.PetId != w.PetId
                || saveRegistered.OperationId is not > 0 || saveRegistered.CoordinatorId != w.CoordinatorId
                || !events.Any(e => e.Kind == "CoordinatorStopped" && e.CoordinatorId == w.CoordinatorId && e.WindowId == w.WindowId && e.Value == "StoppedWithoutFailure")) throw Reject();
            if (i == 1 && (windows[0].SessionId == w.SessionId || windows[0].ClientId == w.ClientId || windows[0].CoordinatorId == w.CoordinatorId)) throw Reject();
        }
        var canceledPerWindow = new Dictionary<int, int>();
        foreach (var r in requests)
        {
            var rows = events.Where(e => e.RequestSequence == r.Sequence).ToArray();
            var registered = rows.Single(e => e.Kind == "RequestRegistered"); var terminal = rows.Single(e => e.Kind == "RequestTerminal");
            if (terminal.Sequence <= registered.Sequence || terminal.Value == "Faulted" || rows.Any(e => e.OperationId != registered.OperationId || e.CoordinatorId != registered.CoordinatorId)) throw Reject();
            var before = rows.Where(e => e.Kind == "SendBefore").ToArray(); var after = rows.Where(e => e.Kind == "SendAfter").ToArray();
            var headers = rows.Where(e => e.Kind == "HeadersReceived").ToArray(); var bodies = rows.Where(e => e.Kind == "BodyValidated").ToArray();
            if (before.Length != after.Length || before.Length > 1 || before.Length == 1 && (before[0].Sequence <= registered.Sequence || after[0].Sequence <= before[0].Sequence)) throw Reject();
            if (headers.Length > 1 || bodies.Length > 1 || before.Length == 0 && (headers.Length != 0 || r.ResponseStatus is not null)
                || headers.Length == 1 && (before.Length != 1 || headers[0].Sequence <= after[0].Sequence || headers[0].Sequence >= terminal.Sequence || r.ResponseStatus is null)
                || headers.Length == 0 && r.ResponseStatus is not null) throw Reject();
            if (r.ResponseStatus is { } status && (status < 200 || status >= 300)) throw Reject();
            if (terminal.Value == "RanToCompletion")
            {
                if (before.Length != 1 || r.ResponseStatus is null || headers.Length != 1 || bodies.Length != 1 || bodies[0].Sequence <= headers[0].Sequence || bodies[0].Sequence >= terminal.Sequence
                    || r.Path is "/api/v1/me/pet" or "/api/v1/me/pet/configuration" && r.ResponseVersion is not > 0) throw Reject();
                continue;
            }
            if (terminal.Value != "Canceled" || terminal.WindowId is null || registered.OperationId is null || registered.CoordinatorId is null) throw Reject();
            if (events.Count(e => e.Kind == "RequestRegistered" && e.OperationId == registered.OperationId) != 1) throw Reject();
            var w = windows.Single(w => w.WindowId == terminal.WindowId);
            var invoked = events.Single(e => e.Kind == "UiaLogoutInvoked" && e.WindowId == w.WindowId);
            var closed = events.Single(e => e.Kind == "WindowClosed" && e.WindowId == w.WindowId);
            var operation = events.Where(e => e.OperationId == registered.OperationId && e.RequestSequence is null).ToArray();
            var start = operation.Single(e => e.Kind == "OperationStarted"); var cancel = operation.Single(e => e.Kind == "OperationCallerCancelled");
            var opTerminal = operation.Single(e => e.Kind == "OperationTerminal"); var caller = rows.Single(e => e.Kind == "CallerCancelled");
            var stopped = operation.Single(e => e.Kind == "CoordinatorStopped"); var matched = rows.Single(e => e.Kind == "RequestCanceledTokenMatched");
            var logoutTerminal = events.Single(e => e.Kind == "RequestTerminal" && e.RequestSequence == w.LogoutRequestSequence);
            if (r.Role != "desktop" || r.Actor != "b" || r.Method != "GET" || r.Path != "/api/v1/me/pet" || r.Retired || r.PreviousPumpSettled != true || r.PetReadOrdinal is not >= 2
                || r.ExpectedVersion is not null || r.ConfigurationSha256 is not null || r.ResponseVersion is not null || r.ClientId != w.ClientId || r.UserId != w.UserId || r.PetId != w.PetId || r.SessionId != w.SessionId
                || r.Sequence <= w.SaveRequestSequence || r.Sequence >= w.LogoutRequestSequence || registered.CoordinatorId != w.CoordinatorId
                || phases.Any(p => p.RequestSequence == r.Sequence) || opTerminal.Value != "Canceled" || opTerminal.Sequence >= closed.Sequence || opTerminal.WindowId != w.WindowId
                || caller.Sequence <= invoked.Sequence || caller.Sequence >= logoutTerminal.Sequence || caller.WindowId != w.WindowId
                || terminal.Sequence >= closed.Sequence || cancel.Sequence <= invoked.Sequence || cancel.Sequence >= logoutTerminal.Sequence || cancel.WindowId != w.WindowId
                || (decimal)cancel.Tick - start.Tick >= (decimal)frequency * 20 || cancel.Tick < start.Tick
                || stopped.WindowId != w.WindowId || stopped.Value != "StoppedWithoutFailure" || stopped.Sequence <= invoked.Sequence || stopped.Sequence >= cancel.Sequence
                || rows.Any(e => e.Kind is "FixtureCancelled" or "BodyDeadlineCancelled" or "BodyValidated")
                || matched.Value != "true" || matched.Sequence <= caller.Sequence || matched.Sequence >= terminal.Sequence) throw Reject();
            if (canceledPerWindow.GetValueOrDefault(w.WindowId) >= 1) throw Reject(); canceledPerWindow[w.WindowId] = 1;
        }
    }
    private static InvalidOperationException Reject() => new("accountsync_b1c2_lifecycle_rejected");
}
