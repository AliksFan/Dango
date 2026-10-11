namespace Dango.AccountSyncReview.Tests;

internal static class PorchCrudC1EvidenceContract
{
    internal static void VerifyRequests(IReadOnlyList<PorchCrudC1Request> requests, Guid a, Guid b, Guid room, long frequency)
    {
        if (a == Guid.Empty || b == Guid.Empty || a == b || room == Guid.Empty || frequency <= 0 || requests.Count is < 25 or > 128) throw Reject();
        long? previousAfter = null; var index = 0;
        foreach (var row in requests)
        {
            if (row.Sequence != ++index || row.Actor is not (null or "a" or "b") || row.Role is not ("registration" or "desktop" or "outsider")) throw Reject();
            if (row.SendBefore is { } before && row.SendAfter is { } after)
            {
                if (before < 0 || after < before || previousAfter is { } previous && (before - previous) / (double)frequency < .6) throw Reject();
                previousAfter = after;
            }
            else if (row.SendBefore is not null || row.SendAfter is not null) throw Reject();
            if (row.Terminal == "Canceled")
            {
                // Only the production background read may be cancelled by actual logout.
                if (row.Method != "GET" || row.Path != "/api/v1/me/pet" || row.Actor != "a" || row.SessionId != a || row.Operation is not null) throw Reject();
            }
            else if (row.Terminal != "RanToCompletion" || row.Status is null || row.SendBefore is null) throw Reject();
        }
        var writes = requests.Where(r => r.Path.StartsWith("/api/v1/rooms", StringComparison.Ordinal) && r.Method != "GET").ToArray();
        string[] actors = ["a", "b", "a", "b", "a", "b", "a"];
        string[] kinds = ["Create", "Update", "Update", "Dissolve", "Dissolve", "Restore", "Restore"];
        long?[] expected = [null, 1, 1, 2, 2, 3, 3];
        int[] statuses = [201, 404, 200, 404, 200, 404, 200];
        long?[] versions = [1, null, 2, null, 3, null, 4];
        if (writes.Length != 7 || writes.Any(r => r.RequestId is null || r.RequestId == Guid.Empty) || writes.Select(r => r.RequestId).Distinct().Count() != 7) throw Reject();
        for (var i = 0; i < 7; i++)
        {
            var row = writes[i]; var suffix = kinds[i] switch { "Dissolve" => "/dissolve", "Restore" => "/restore", _ => "" };
            var path = i == 0 ? "/api/v1/rooms" : $"/api/v1/rooms/{room:D}{suffix}";
            if (row.Actor != actors[i] || row.Operation != kinds[i] || row.ExpectedVersion != expected[i] || row.Status != statuses[i]
                || row.RoomVersion != versions[i] || row.Path != path || row.SessionId != (actors[i] == "a" ? a : b)
                || row.Terminal != "RanToCompletion") throw Reject();
            if (actors[i] == "a")
            {
                var readback = requests.FirstOrDefault(r => r.Sequence > row.Sequence && r.Method == "GET" && r.Path == $"/api/v1/rooms/{room:D}" && r.Actor == "a");
                if (readback is null || readback.Status != 200 || readback.RoomVersion != versions[i] || readback.Terminal != "RanToCompletion") throw Reject();
            }
        }
        if (requests.Count(r => r.Actor == "b" && r.Method == "GET" && r.Path == $"/api/v1/rooms/{room:D}" && r.Status == 404) != 1
            || requests.Count(r => r.Actor == "a" && r.Method == "GET" && r.Path == $"/api/v1/rooms/{room:D}" && r.Status == 200 && r.RoomVersion == 4) != 2
            || requests.Count(r => r.Operation is not null) != 17
            || requests.Any(r => r.Status is { } status && status >= 400 && !(r.Actor == "b" && r.Path.StartsWith($"/api/v1/rooms/{room:D}", StringComparison.Ordinal) && status == 404))) throw Reject();
    }
    private static InvalidOperationException Reject() => new("porch_c1_evidence_rejected");
}
