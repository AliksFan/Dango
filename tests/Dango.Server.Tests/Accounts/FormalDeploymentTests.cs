using Dango.Server.Configuration;
using Dango.Server.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
public sealed class FormalDeploymentTests
{
    [TestMethod]
    public void ExecutableCheckoutIsProtectedIndependentlyOfWorkingDirectory()
    {
        var root = FormalDeploymentPaths.ProtectedRoot();
        Assert.IsTrue(File.Exists(Path.Combine(root, "src", "Dango.Server", "Dango.Server.csproj")));
        Assert.IsFalse(FormalServerOptions.TryCreate("dango_fictional", "Host=127.0.0.1;Database=dango_fictional;Username=fictional;Password=fictional_test_only",
            Path.Combine(root, "artifacts", "fictional-key-directory"), "https://127.0.0.1:443/", root, out _, out var code));
        Assert.AreEqual("formal_key_directory_invalid", code);
    }
}
