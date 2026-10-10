using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncBoundary")]
public sealed class AccountSyncRequestPolicyTests
{
    private static readonly Uri Origin = new("https://127.0.0.1:62123/");
    private static readonly Guid Registration = Guid.Parse("7be4f2ba-0abc-4b72-b6cb-1fdc89ef82d1");
    private static readonly Guid OtherRegistration = Guid.Parse("44a12d82-3c42-4265-82b4-470207b8a169");

    [TestMethod]
    [DataRow("POST", "/api/v1/auth/registrations", AccountSyncOperation.Start)]
    [DataRow("POST", "/api/v1/auth/login", AccountSyncOperation.Login)]
    [DataRow("POST", "/api/v1/auth/logout", AccountSyncOperation.Logout)]
    [DataRow("PATCH", "/api/v1/me/profile", AccountSyncOperation.Profile)]
    [DataRow("PATCH", "/api/v1/me/pet/configuration", AccountSyncOperation.Configuration)]
    public void Approved_mutations_have_the_exact_operation(string method, string path, AccountSyncOperation expected)
    {
        Assert.AreEqual(expected, AccountSyncRequestPolicy.Classify(Origin, new(Origin, path), method, null));
        Assert.AreEqual(expected, AccountSyncRequestPolicy.ClassifyPath(method, path, null));
    }

    [TestMethod]
    [DataRow("/api/v1/auth/request-protection")]
    [DataRow("/api/v1/me")]
    [DataRow("/api/v1/me/profile")]
    [DataRow("/api/v1/me/pet")]
    public void Approved_reads_are_not_business_mutations(string path)
    {
        Assert.IsNull(AccountSyncRequestPolicy.Classify(Origin, new(Origin, path), "GET", null));
        Assert.IsNull(AccountSyncRequestPolicy.ClassifyPath("GET", path, null));
    }

    [TestMethod]
    public void Only_the_captured_registration_has_status_and_completion_routes()
    {
        var status = $"/api/v1/auth/registrations/{Registration:D}/status";
        var complete = $"/api/v1/auth/registrations/{Registration:D}/complete";
        Assert.IsNull(AccountSyncRequestPolicy.Classify(Origin, new(Origin, status), "GET", Registration));
        Assert.IsNull(AccountSyncRequestPolicy.ClassifyPath("GET", status, Registration));
        Assert.AreEqual(AccountSyncOperation.Complete, AccountSyncRequestPolicy.Classify(Origin, new(Origin, complete), "POST", Registration));
        Assert.AreEqual(AccountSyncOperation.Complete, AccountSyncRequestPolicy.ClassifyPath("POST", complete, Registration));
    }

    [TestMethod]
    public void Matching_requests_use_the_supplied_fixed_port()
    {
        var alternate = new Uri("https://127.0.0.1:62124/");
        Assert.AreEqual(AccountSyncOperation.Login, AccountSyncRequestPolicy.Classify(alternate,
            new(alternate, "/api/v1/auth/login"), "POST", null));
        Rejected(() => AccountSyncRequestPolicy.Classify(alternate, new(Origin, "/api/v1/auth/login"), "POST", null));
    }

    [TestMethod]
    [DataRow("http://127.0.0.1:62123/")]
    [DataRow("https://localhost:62123/")]
    [DataRow("https://[::1]:62123/")]
    [DataRow("https://192.0.2.1:62123/")]
    [DataRow("https://example.invalid:62123/")]
    [DataRow("https://127.0.0.1:0/")]
    [DataRow("https://fictional:secret@127.0.0.1:62123/")]
    [DataRow("https://127.0.0.1:62123/api/")]
    [DataRow("https://127.0.0.1:62123/?fictional=1")]
    [DataRow("https://127.0.0.1:62123/#fictional")]
    [DataRow("/relative-origin")]
    public void Invalid_origins_reject_even_an_approved_path(string endpoint)
    {
        Rejected(() => AccountSyncRequestPolicy.Classify(new(endpoint, UriKind.RelativeOrAbsolute),
            new(Origin, "/api/v1/me"), "GET", Registration));
    }

    [TestMethod]
    [DataRow("http://127.0.0.1:62123/api/v1/me")]
    [DataRow("https://localhost:62123/api/v1/me")]
    [DataRow("https://[::1]:62123/api/v1/me")]
    [DataRow("https://192.0.2.1:62123/api/v1/me")]
    [DataRow("https://example.invalid:62123/api/v1/me")]
    [DataRow("https://127.0.0.1:62124/api/v1/me")]
    [DataRow("https://127.0.0.1:0/api/v1/me")]
    [DataRow("https://fictional:secret@127.0.0.1:62123/api/v1/me")]
    [DataRow("https://127.0.0.1:62123/api/v1/me?fictional=1")]
    [DataRow("https://127.0.0.1:62123/api/v1/me#fictional")]
    [DataRow("/api/v1/me")]
    [DataRow("//127.0.0.1:62123/api/v1/me")]
    public void Requests_cannot_leave_the_exact_origin_or_add_credentials_query_or_fragment(string request)
    {
        Rejected(() => AccountSyncRequestPolicy.Classify(Origin, new(request, UriKind.RelativeOrAbsolute), "GET", Registration));
    }

    [TestMethod]
    [DataRow("GET", "/api/v1/auth/login")]
    [DataRow("GET", "/api/v1/auth/logout")]
    [DataRow("GET", "/api/v1/auth/registrations")]
    [DataRow("GET", "/api/v1/me/pet/configuration")]
    [DataRow("POST", "/api/v1/me/profile")]
    [DataRow("PUT", "/api/v1/me/profile")]
    [DataRow("DELETE", "/api/v1/me/profile")]
    [DataRow("POST", "/api/v1/me/pet/configuration")]
    [DataRow("PATCH", "/api/v1/auth/login")]
    [DataRow("HEAD", "/api/v1/me")]
    [DataRow("OPTIONS", "/api/v1/me")]
    [DataRow("post", "/api/v1/auth/login")]
    [DataRow("get", "/api/v1/me")]
    [DataRow(" GET", "/api/v1/me")]
    [DataRow("POST ", "/api/v1/auth/login")]
    [DataRow("", "/api/v1/me")]
    [DataRow("GET", "/api/v1/me/")]
    [DataRow("GET", "/API/v1/me")]
    [DataRow("GET", "/api/v1//me")]
    [DataRow("GET", "/api/v1/rooms")]
    [DataRow("POST", "/api/v1/rooms")]
    [DataRow("POST", "/api/v1/rooms/fictional/join")]
    [DataRow("POST", "/api/v1/auth/registrations/fictional/complete")]
    [DataRow("POST", "/api/v1/auth/password-reset")]
    [DataRow("GET", "/health/ready")]
    [DataRow("GET", "/unknown")]
    public void Unknown_routes_and_wrong_methods_are_rejected_by_both_boundaries(string method, string path)
    {
        Rejected(() => AccountSyncRequestPolicy.Classify(Origin, new(Origin, path), method, Registration));
        Rejected(() => AccountSyncRequestPolicy.ClassifyPath(method, path, Registration));
    }

    [TestMethod]
    [DataRow("GET", "status")]
    [DataRow("POST", "complete")]
    public void Registration_routes_reject_missing_empty_other_and_noncanonical_identifiers(string method, string action)
    {
        var exact = $"/api/v1/auth/registrations/{Registration:D}/{action}";
        foreach (var captured in new Guid?[] { null, Guid.Empty, OtherRegistration })
        {
            Rejected(() => AccountSyncRequestPolicy.Classify(Origin, new(Origin, exact), method, captured));
            Rejected(() => AccountSyncRequestPolicy.ClassifyPath(method, exact, captured));
        }
        foreach (var path in new[]
        {
            $"/api/v1/auth/registrations/{OtherRegistration:D}/{action}",
            $"/api/v1/auth/registrations/{Guid.Empty:D}/{action}",
            $"/api/v1/auth/registrations/{Registration:N}/{action}",
            $"/api/v1/auth/registrations/{Registration.ToString("D").ToUpperInvariant()}/{action}",
            $"/api/v1/auth/registrations/{Registration:D}/{action}/",
            $"/api/v1/auth/registrations/{Registration:D}/{action}/extra",
            exact.ToUpperInvariant()
        })
        {
            Rejected(() => AccountSyncRequestPolicy.Classify(Origin, new(Origin, path), method, Registration));
            Rejected(() => AccountSyncRequestPolicy.ClassifyPath(method, path, Registration));
        }
        var wrongMethod = method == "GET" ? "POST" : "GET";
        Rejected(() => AccountSyncRequestPolicy.ClassifyPath(wrongMethod, exact, Registration));
        Rejected(() => AccountSyncRequestPolicy.Classify(Origin, new(Origin, exact), wrongMethod, Registration));
    }

    [TestMethod]
    public void Server_path_policy_rejects_url_query_fragment_relative_and_null_input()
    {
        foreach (var path in new string?[] { null, "", "api/v1/me", "https://127.0.0.1:62123/api/v1/me", "/api/v1/me?fictional=1", "/api/v1/me#fictional" })
            Rejected(() => AccountSyncRequestPolicy.ClassifyPath("GET", path, Registration));
        Rejected(() => AccountSyncRequestPolicy.ClassifyPath(null, "/api/v1/me", Registration));
        Rejected(() => AccountSyncRequestPolicy.Classify(Origin, new(Origin, "/api/v1/me"), null, Registration));
        Rejected(() => AccountSyncRequestPolicy.Classify(null, new(Origin, "/api/v1/me"), "GET", Registration));
        Rejected(() => AccountSyncRequestPolicy.Classify(Origin, null, "GET", Registration));
    }

    private static void Rejected(Action action)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(action);
        Assert.AreEqual("accountsync_request_policy_rejected", error.Message);
    }
}
