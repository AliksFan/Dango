using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Dango.BackendManualReview;

public interface IManualReviewCredentialResources
{
    bool Exists(string exactTarget);
    void Delete(string exactTarget);
}
public sealed record OwnedCredentialSlots(Uri Origin, string SessionTarget, string RegistrationTarget);
public sealed class ManualReviewCredentials(IManualReviewCredentialResources? resources = null)
{
    private readonly IManualReviewCredentialResources backend = resources ?? new WindowsCredentialResources();
    private readonly HashSet<OwnedCredentialSlots> owned = new(ReferenceEqualityComparer.Instance);
    private readonly object gate = new();

    // Keep precisely the product's authority normalization and uppercase SHA256 encoding.
    public static string Target(Uri origin, string slot)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != "https" || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || slot is not ("session" or "registration")) throw new InvalidDataException("manual_credential_target_invalid");
        var canonical = origin.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return $"Dango/EmailBackend/v1/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}/{slot}";
    }

    public OwnedCredentialSlots ReserveUnusedOrigin(bool authorizationVerified, Uri origin)
    {
        RequireAuthorization(authorizationVerified);
        RequireManualOrigin(origin);
        var session = Target(origin, "session"); var registration = Target(origin, "registration");
        lock (gate)
        {
            if (owned.Any(entry => entry.Origin == origin)) throw new InvalidOperationException("manual_credential_origin_already_owned");
            var sessionExists = backend.Exists(session);
            var registrationExists = backend.Exists(registration);
            if (sessionExists || registrationExists) throw new InvalidOperationException("manual_credential_slot_already_exists");
            var receipt = new OwnedCredentialSlots(origin, session, registration);
            owned.Add(receipt);
            return receipt;
        }
    }

    public void ClearOnlyOwned(bool authorizationVerified, OwnedCredentialSlots slots)
    {
        RequireAuthorization(authorizationVerified);
        lock (gate)
        {
            if (!owned.Contains(slots)) throw new InvalidOperationException("manual_credential_ownership_unproven");
            backend.Delete(slots.SessionTarget);
            backend.Delete(slots.RegistrationTarget);
            owned.Remove(slots);
        }
    }

    internal static void RequireAuthorization(bool verified)
    {
        if (!verified) throw new InvalidOperationException("manual_resource_authorization_required");
    }

    internal static void RequireManualOrigin(Uri origin)
    {
        _ = Target(origin, "session");
        if (origin.Host != "127.0.0.1" || origin.Port is <= 0 or > 65535)
            throw new InvalidDataException("manual_origin_invalid");
    }

    private sealed class WindowsCredentialResources : IManualReviewCredentialResources
    {
        private const int NotFound = 1168;
        public bool Exists(string exactTarget)
        {
            if (!CredRead(exactTarget, 1, 0, out var pointer))
            {
                if (Marshal.GetLastWin32Error() == NotFound) return false;
                throw new IOException("manual_credential_existence_failed");
            }
            // The existence probe never marshals the credential or copies its blob.
            try { return true; }
            finally { CredFree(pointer); }
        }

        public void Delete(string exactTarget)
        {
            if (!CredDelete(exactTarget, 1, 0) && Marshal.GetLastWin32Error() != NotFound)
                throw new IOException("manual_credential_delete_failed");
        }
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    }
}
