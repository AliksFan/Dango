namespace Dango.AccountSyncReview.Tests;

// A route label alone never grants arbitrary room or account access.
internal static class PorchCrudC1RequestPolicy
{
    internal static PorchCrudC1Operation? Classify(Uri endpoint, Uri uri, string method, Guid? registration, Guid? room)
    {
        if (!endpoint.IsAbsoluteUri || !uri.IsAbsoluteUri || endpoint.Scheme != "https" || uri.Scheme != "https"
            || endpoint.Host != "127.0.0.1" || uri.Host != endpoint.Host || endpoint.Port != uri.Port
            || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.UserInfo.Length != 0
            || endpoint.Fragment.Length != 0 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw Reject();
        var path = uri.AbsolutePath;
        if (method == "GET" && path == "/api/v1/me/rooms")
        {
            if (uri.Query is not ("?state=active&limit=4" or "?state=dissolved&limit=4")) throw Reject();
            return null;
        }
        if (uri.Query.Length != 0) throw Reject();
        if (room is { } id && id != Guid.Empty)
        {
            if (path == $"/api/v1/rooms/{id:D}") return method switch
            { "GET" => null, "PATCH" => PorchCrudC1Operation.Update, _ => throw Reject() };
            if (method == "POST" && path == $"/api/v1/rooms/{id:D}/dissolve") return PorchCrudC1Operation.Dissolve;
            if (method == "POST" && path == $"/api/v1/rooms/{id:D}/restore") return PorchCrudC1Operation.Restore;
        }
        if (method == "POST" && path == "/api/v1/rooms") return PorchCrudC1Operation.Create;
        var operation = AccountSyncRequestPolicy.Classify(endpoint, uri, method, registration);
        return operation switch
        {
            null => null,
            AccountSyncOperation.Start => PorchCrudC1Operation.Start,
            AccountSyncOperation.Complete => PorchCrudC1Operation.Complete,
            AccountSyncOperation.Login => PorchCrudC1Operation.Login,
            AccountSyncOperation.Profile => PorchCrudC1Operation.Profile,
            AccountSyncOperation.Logout => PorchCrudC1Operation.Logout,
            _ => throw Reject()
        };
    }
    private static InvalidOperationException Reject() => new("porch_c1_request_policy_rejected");
}
