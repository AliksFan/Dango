using System.Security.AccessControl;
using System.Security.Principal;

namespace Dango.Server.Hosting;

internal static class FormalKeyDirectory
{
    public static void Protect(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("formal_key_protection_not_configured");
        var directory = Directory.CreateDirectory(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("formal_key_directory_invalid");
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("formal_key_owner_invalid");
        var acl = new DirectorySecurity();
        acl.SetOwner(user);
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }
}
