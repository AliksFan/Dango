using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalPetConfigurationRepositoryTests
{
    private const string Target = """{"appearanceSchemaVersion":3,"bodyColorHex":"#eeccdd","eyeColorHex":"#334455","eyeSizePercent":110,"eyeOvalityPercent":10,"eyeHeightControlPercent":50,"eyeSpacingControlPercent":25}""";

    [STATestMethod]
    public void LegacyRawBytesArePreservedWhileNormalizationAndExplicitApplyUseSchemaThree()
    {
        var path = NewPath();
        const string legacy = "{ \"appearanceSchemaVersion\":1, \"bodyColorHex\":\"#112233\" }";
        File.WriteAllText(path, legacy);
        var session = new AppearanceSettingsSession(path);
        var repository = new LocalPetConfigurationRepository(path, session, Dispatcher.CurrentDispatcher);
        var captured = Run(repository.CaptureAsync(default));
        CollectionAssert.AreEqual(File.ReadAllBytes(path), captured.OriginalBytes.ToArray());
        Assert.AreEqual(3, captured.NormalizedConfiguration.GetProperty("appearanceSchemaVersion").GetInt32());
        Assert.AreEqual(legacy, File.ReadAllText(path), "Reading must not migrate a legacy file.");
        Run(repository.ApplyAsync(captured, Parse(Target), default));
        Assert.AreEqual("#EECCDD", session.SavedBodyColor.ToHex());
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(legacy, System.Text.Encoding.UTF8.GetString(captured.OriginalBytes.Span));
        Assert.IsTrue(AppearanceContract.Equivalent(Parse(Target), Parse(File.ReadAllText(path))));
    }

    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ExistingUtf16AndUtf32BomFilesRemainReadableWithoutRewritingTheirOriginalBytes(bool utf32, bool bigEndian)
    {
        var path = NewPath();
        System.Text.Encoding encoding = utf32 ? new System.Text.UTF32Encoding(bigEndian, true)
            : new System.Text.UnicodeEncoding(bigEndian, true);
        var original = encoding.GetPreamble().Concat(encoding.GetBytes(Target.Replace("#eeccdd", "#EECCDD"))).ToArray();
        File.WriteAllBytes(path, original);
        var session = new AppearanceSettingsSession(path);
        Assert.IsFalse(session.IsWriteProtected);
        Assert.AreEqual("#EECCDD", session.SavedBodyColor.ToHex());
        var repository = new LocalPetConfigurationRepository(path, session, Dispatcher.CurrentDispatcher);
        var captured = Run(repository.CaptureAsync(default));
        CollectionAssert.AreEqual(original, captured.OriginalBytes.ToArray());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsTrue(AppearanceContract.Equivalent(Parse(Target), captured.NormalizedConfiguration));
    }

    [STATestMethod]
    public void DraftAndCorruptFileCannotBeSilentlyUploadedAsDefaultAppearance()
    {
        var path = NewPath();
        var session = new AppearanceSettingsSession(path);
        var repository = new LocalPetConfigurationRepository(path, session, Dispatcher.CurrentDispatcher);
        Assert.IsFalse(Run(repository.CaptureAsync(default)).Exists);
        session.StageBodyPreview(OpaqueSrgbColor.Parse("#123456"));
        AssertCode("local_unsaved_changes", () => Run(repository.CaptureAsync(default)));
        session.CancelPreview();
        File.WriteAllText(path, "{\"appearanceSchemaVersion\":99}");
        var original = File.ReadAllBytes(path);
        AssertCode("local_configuration_invalid", () => Run(repository.CaptureAsync(default)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [STATestMethod]
    public void ExternalChangesAndLockedDiskPreserveOriginalAndSavedSession()
    {
        var path = NewPath();
        var session = new AppearanceSettingsSession(path);
        var repository = new LocalPetConfigurationRepository(path, session, Dispatcher.CurrentDispatcher);
        var expected = Run(repository.CaptureAsync(default));
        File.WriteAllText(path, Target.Replace("#eeccdd", "#EECCDD"));
        AssertCode("local_configuration_changed", () => Run(repository.ApplyAsync(expected, Parse(Target), default)));
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.SavedAppearance);
        var changed = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            AssertCode("local_configuration_unavailable", () => Run(repository.CaptureAsync(default)));
        CollectionAssert.AreEqual(changed, File.ReadAllBytes(path));
    }

    [STATestMethod]
    public void OversizedAppearanceCannotCrashOfflineStartupOrDiscardTheOriginal()
    {
        var path = NewPath();
        var original = new byte[65537];
        File.WriteAllBytes(path, original);
        var session = new AppearanceSettingsSession(path);
        Assert.IsTrue(session.IsWriteProtected);
        var repository = new LocalPetConfigurationRepository(path, session, Dispatcher.CurrentDispatcher);
        AssertCode("local_configuration_invalid", () => Run(repository.CaptureAsync(default)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    private static void AssertCode(string code, Action action) =>
        Assert.AreEqual(code, Assert.ThrowsExactly<PetConfigurationChoiceException>(action).Code);
    private static JsonElement Parse(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    private static T Run<T>(Task<T> task) { Wait(task); return task.GetAwaiter().GetResult(); }
    private static void Run(Task task) { Wait(task); task.GetAwaiter().GetResult(); }
    private static void Wait(Task task)
    {
        var until = Environment.TickCount64 + 2000;
        while (!task.IsCompleted && Environment.TickCount64 < until)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        Assert.IsTrue(task.IsCompleted);
    }
    private static string NewPath()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "local-room", "b2-appearance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return Path.Combine(root, "appearance.json");
    }
}
