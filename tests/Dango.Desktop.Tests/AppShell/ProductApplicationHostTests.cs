using System.Reflection;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductApplicationHostTests
{
    private static string NewPath()
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "application", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    private static T Field<T>(object host, string name) =>
        (T)host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BothExitPathsKeepHatInteractionAndLegacySkeletonPreference(bool createControlCenter)
    {
        var path = NewPath();
        File.WriteAllText(path, """{"schemaVersion":3,"width":1000,"height":700,"showDebugSkeleton":true}""");
        using (var host = new DangoApplicationHost(settingsFile: path))
        {
            var commands = Field<ApplicationCommandController>(host, "commandController");
            Assert.AreEqual(ApplicationUiMode.Product, commands.UiMode);
            Assert.IsFalse(commands.IsDebugSkeletonVisible);
            Assert.AreEqual("Wallpaper", commands.GetSnapshot().Mode);
            Assert.IsFalse(commands.GetKeyboardControlSettings().IsEnabled);
            Assert.AreEqual(KeyboardControlScheme.ArrowKeys, commands.GetKeyboardControlSettings().Scheme);
            Assert.AreEqual(CommandApplicationState.Applied, commands.ApplyHatPreset("bowler").State);
            Assert.AreEqual(CommandApplicationState.Applied, commands.SetInteractionEnabled(false).State);
            if (createControlCenter)
            {
                var window = (ControlCenterWindow)typeof(DangoApplicationHost)
                    .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
                window.ShowOverview();
                window.ShowDiagnostics();
            }
            Assert.AreEqual(0, Field<StackedDangoSceneRuntime>(host, "stackedRuntime").EntityIds.Count,
                "Reading overview and diagnostics must not implicitly create the world.");
        }
        var restored = new ControlCenterWindowSettingsStore().Load(path);
        Assert.AreEqual("bowler", restored.HatPresetId);
        Assert.IsFalse(restored.InteractionEnabled);
        Assert.IsTrue(restored.ShowDebugSkeleton, "Runtime false must not replace the compatibility value.");
        Assert.IsFalse(restored.IntroSeen, "Constructing or refreshing a hidden window does not count as presenting the introduction.");
    }

    [STATestMethod]
    public void ProductShellCanInspectMissingModuleAndExitWithoutOverwritingFutureSettings()
    {
        var path = NewPath();
        const string original = """{"schemaVersion":999,"futureSetting":"fictional"}""";
        File.WriteAllText(path, original);
        using (var host = new DangoApplicationHost(settingsFile: path, nativeLibraryPath: path + ".missing.dll"))
        {
            var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
            Assert.IsFalse(runtime.TryInitialize());
            var commands = Field<ApplicationCommandController>(host, "commandController");
            Assert.IsNotNull(commands.GetSnapshot().LastError);
            Assert.AreEqual(CommandApplicationState.Failed, commands.SetVisibility(true).State);
            StringAssert.Contains(commands.CreateDiagnosticSummary(), "Unavailable");
            Assert.AreEqual(CommandApplicationState.Failed, commands.RetryInitialization().State);
            Assert.AreEqual(0, runtime.EntityIds.Count);
        }
        Assert.AreEqual(original, File.ReadAllText(path));
    }
}
