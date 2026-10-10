namespace Dango.AccountSyncReview.Tests;

// Pure request allowlist shared by the controlled client and server boundary.
// Classification does not reserve an attempt or touch any runtime resource.
internal static class AccountSyncRequestPolicy
{
    internal static AccountSyncOperation? Classify(Uri? endpoint, Uri? request, string? method, Guid? registeredId)
    {
        if (endpoint is null || request is null || !IsLoopbackHttps(endpoint) || endpoint.AbsolutePath != "/"
            || !IsLoopbackHttps(request) || request.Port != endpoint.Port)
            throw Reject();
        return ClassifyPath(method, request.AbsolutePath, registeredId);
    }

    internal static AccountSyncOperation? ClassifyPath(string? method, string? path, Guid? registeredId) =>
        (method, path) switch
        {
            ("POST", "/api/v1/auth/registrations") => AccountSyncOperation.Start,
            ("POST", "/api/v1/auth/login") => AccountSyncOperation.Login,
            ("POST", "/api/v1/auth/logout") => AccountSyncOperation.Logout,
            ("PATCH", "/api/v1/me/profile") => AccountSyncOperation.Profile,
            ("PATCH", "/api/v1/me/pet/configuration") => AccountSyncOperation.Configuration,
            ("GET", "/api/v1/auth/request-protection" or "/api/v1/me" or "/api/v1/me/profile" or "/api/v1/me/pet") => null,
            _ => ClassifyRegistration(method, path, registeredId)
        };

    private static AccountSyncOperation? ClassifyRegistration(string? method, string? path, Guid? registeredId)
    {
        if (registeredId is not { } id || id == Guid.Empty) throw Reject();
        if (method == "GET" && path == $"/api/v1/auth/registrations/{id:D}/status") return null;
        if (method == "POST" && path == $"/api/v1/auth/registrations/{id:D}/complete") return AccountSyncOperation.Complete;
        throw Reject();
    }

    private static bool IsLoopbackHttps(Uri uri) => uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "127.0.0.1" && uri.Port > 0
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private static InvalidOperationException Reject() => new("accountsync_request_policy_rejected");
}
