namespace Dango.Server.Hosting;

public static class FormalDeploymentPaths
{
    // CWD is operator-controlled and cannot identify the executable's checkout.
    // Published installations protect their application directory; source builds protect the checkout.
    public static string ProtectedRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if ((File.Exists(Path.Combine(directory.FullName, ".git")) || Directory.Exists(Path.Combine(directory.FullName, ".git")))
                && File.Exists(Path.Combine(directory.FullName, "src", "Dango.Server", "Dango.Server.csproj")))
                return directory.FullName;
        return Path.GetFullPath(AppContext.BaseDirectory);
    }
}
