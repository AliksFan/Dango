using Dango.Desktop.AppShell;
using System.Text.Json;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopStartupOptionsTests
{
    private static readonly string[] DiagnosticModes =
    [
        "--home-ui-review", "--settings-ui-review", "--physics-host-review", "--eye-hat-review",
        "--soft-body-preview", "--paired-contact-preview", "--box2d-native-probe", "--smoke-test",
    ];

    [TestMethod]
    public void OrdinaryStartupHasOneProductRouteAndNoSettingsOverride()
    {
        var options = App.ParseStartupArguments([]);
        Assert.AreEqual(DesktopStartupMode.Product, options.Mode);
        Assert.IsNull(options.SettingsFile);
        Assert.AreEqual(App.ResolveSingleInstanceName([]), options.InstanceName);
    }

    [TestMethod]
    public void EachRetainedDiagnosticIsAStandaloneModeWithoutProductData()
    {
        foreach (var flag in DiagnosticModes)
        {
            var options = App.ParseStartupArguments([flag]);
            Assert.AreNotEqual(DesktopStartupMode.Product, options.Mode, flag);
            Assert.IsNull(options.SettingsFile, flag);
        }
    }

    [TestMethod]
    public void ConflictingDuplicateOrScopedDiagnosticsFailBeforeAnyDataAccess()
    {
        var missing = Path.Combine(Path.GetTempPath(), "Dango-FictionalMissing-" + Guid.NewGuid().ToString("N"));
        foreach (var first in DiagnosticModes)
        {
            foreach (var second in DiagnosticModes)
                Assert.ThrowsExactly<ArgumentException>(() => App.ParseStartupArguments([first, second]), first + second);
            foreach (var scope in new[] { "--data-root", "--local-install-acceptance-settings" })
            {
                Assert.ThrowsExactly<ArgumentException>(() => App.ParseStartupArguments([first, scope, missing]));
                Assert.ThrowsExactly<ArgumentException>(() => App.ParseStartupArguments([scope, missing, first]));
            }
        }
        Assert.IsFalse(Directory.Exists(missing), "Rejected startup must not create or load a data scope.");
    }

    [TestMethod]
    public void RetiredProductRoutesNeverFallThroughToTheDailyProduct()
    {
        string[] retired = ["--unified-live-home-review", "--porch-scene-review", "--local-room-review",
            "--desktop-pet-room-review", "--face-review", "--accessory-review", "--face-review-settings",
            "--accessory-review-settings", "--belt-body-review", "--belt-room-module", "--unknown"];
        foreach (var flag in retired)
        {
            Assert.ThrowsExactly<ArgumentException>(() => App.ParseStartupArguments([flag]), flag);
            Assert.ThrowsExactly<ArgumentException>(() => App.ParseStartupArguments([flag, "fictional"]), flag);
            Assert.ThrowsExactly<ArgumentException>(() => App.ParseStartupArguments(["--smoke-test", flag]), flag);
        }
    }

    [TestMethod]
    public void OwnedAcceptanceAndGeneralDataUseTheSamePathScopedProductIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "Dango-Startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var owner = Path.Combine(root, ".dango-local-install-acceptance.json");
            File.WriteAllText(owner, JsonSerializer.Serialize(new { application = "Dango", acceptanceFormat = 1, fictional = true }));
            var acceptance = App.ParseStartupArguments(["--local-install-acceptance-settings", root]);
            var product = App.ParseStartupArguments(["--data-root", root]);
            Assert.AreEqual(DesktopStartupMode.Product, acceptance.Mode);
            Assert.AreEqual(product, acceptance);
            Assert.AreNotEqual(App.ParseStartupArguments([]).InstanceName, acceptance.InstanceName);
            var alias = App.ParseStartupArguments(["--local-install-acceptance-settings", root.ToUpperInvariant() + Path.DirectorySeparatorChar]);
            Assert.AreEqual(acceptance.Mode, alias.Mode);
            Assert.AreEqual(acceptance.InstanceName, alias.InstanceName);
            Assert.IsTrue(string.Equals(acceptance.SettingsFile, alias.SettingsFile, StringComparison.OrdinalIgnoreCase));
            CollectionAssert.AreEqual(new[] { owner }, Directory.GetFiles(root), "Parsing may inspect ownership, but must not write settings.");
            using var primary = SingleInstanceCoordinator.TryBecomePrimary(acceptance.InstanceName, () => { });
            Assert.IsNotNull(primary);
            using var duplicate = SingleInstanceCoordinator.TryBecomePrimary(product.InstanceName, () => { });
            Assert.IsNull(duplicate, "The same normalized data scope cannot have two primary processes.");
            using var other = SingleInstanceCoordinator.TryBecomePrimary(
                App.ParseStartupArguments(["--data-root", root + "-other"]).InstanceName, () => { });
            Assert.IsNotNull(other, "Independent data scopes must not signal or reuse another primary.");
        }
        finally
        {
            File.Delete(Path.Combine(root, ".dango-local-install-acceptance.json"));
            Directory.Delete(root);
        }
    }

    [TestMethod]
    public void DirectoryIdentityMatchesTheInstalledLauncherContract()
    {
        const string expected = @"Local\Dango.Phase1.App.Data.F0E4A86115DF67A14E248712";
        Assert.AreEqual(expected, App.ParseStartupArguments(["--data-root", @"C:\Fixture\Settings with spaces"]).InstanceName);
        Assert.AreEqual(expected, App.ParseStartupArguments(["--data-root", @"c:\fixture\settings with spaces\"]).InstanceName);
    }

    [STATestMethod]
    public void ProductRejectsLegacyOrRelativeModuleOverridesBeforeCreatingWindows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(nativeLibraryPath: "fictional-legacy.dll"));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(roomModulePath: "fictional-relative-room.dll"));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedPhysicsReview: true,
            roomModulePath: @"C:\Fixture\dango_belt_room.dll"));
    }
}
