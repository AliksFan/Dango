using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.GaitSample;
using System.Reflection;
using System.Text.Json;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class GaitSampleIsolationTests
{
    private static string NewSessionPath() => Path.Combine(TestRepository.Root, "artifacts", "gait-sample", "isolation-test-" + Guid.NewGuid().ToString("N"));
    private static string ModulePath => Path.Combine(AppContext.BaseDirectory, SampleSession.NativeFileName);

    [TestMethod]
    public void OutOfRepositoryAndRootPathsAreRejectedBeforeAnySessionIsCreated()
    {
        foreach (var candidate in new[]
        {
            TestRepository.Root,
            Path.Combine(TestRepository.Root, "artifacts", "gait-sample"),
            Path.Combine(TestRepository.Root, "artifacts", "gait-sample-other", "not-a-session"),
            Path.Combine(TestRepository.Root, "artifacts", "gait-sample", "..", "outside-session"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "settings.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Dango", "gait-sample"),
        })
            Assert.ThrowsExactly<InvalidDataException>(() => SampleSession.ValidateSessionPath(TestRepository.Root, candidate), candidate);
    }

    [TestMethod]
    public void ExistingDirectoryAndFileAreRejectedWithoutChangingSentinel()
    {
        var directory = NewSessionPath();
        Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, "fictional-sentinel.txt");
        File.WriteAllText(sentinel, "preserve fictional original");
        Assert.ThrowsExactly<InvalidDataException>(() => SampleSession.ValidateSessionPath(TestRepository.Root, directory));
        Assert.ThrowsExactly<InvalidDataException>(() => SampleSession.ValidateSessionPath(TestRepository.Root, sentinel));
        Assert.AreEqual("preserve fictional original", File.ReadAllText(sentinel));
    }

    [TestMethod]
    public void MissingOrUnapprovedModuleIsRejectedBeforeSessionCreation()
    {
        var candidate = NewSessionPath();
        Assert.ThrowsExactly<InvalidDataException>(() => SampleSession.Create(TestRepository.Root, candidate, Path.Combine(candidate, "missing.dll")));
        Assert.IsFalse(Directory.Exists(candidate));
        var invalidDirectory = NewSessionPath();
        Directory.CreateDirectory(invalidDirectory);
        var invalidModule = Path.Combine(invalidDirectory, "fictional-invalid.dll");
        File.WriteAllText(invalidModule, "not the approved native module");
        Assert.ThrowsExactly<InvalidDataException>(() => SampleSession.Create(TestRepository.Root, candidate, invalidModule));
        Assert.IsFalse(Directory.Exists(candidate));
    }

    [TestMethod]
    public void FictionalSettingsEnableExistingKeyboardAndWindowWithBothReviewsDisabled()
    {
        var settings = SampleSession.CreateSettings();
        Assert.AreEqual(DesktopDisplayMode.Window, settings.DisplayMode);
        Assert.IsTrue(settings.KeyboardControl.IsEnabled);
        Assert.AreEqual(KeyboardControlScheme.Wasd, settings.KeyboardControl.Scheme);
        Assert.IsTrue(settings.InteractionEnabled);
        Assert.IsTrue(settings.IntroSeen);
        Assert.IsFalse(settings.ShowDebugSkeleton);
        using var session = SampleSession.Create(TestRepository.Root, NewSessionPath(), ModulePath);
        CollectionAssert.AreEqual(new object?[] { false, false, session.SettingsPath, ModulePath, null }, session.HostArguments);
        // Compare the complete persisted value, including nested layering dictionaries.
        // Record equality alone compares those dictionaries by reference after deserialization.
        Assert.AreEqual(JsonSerializer.Serialize(settings),
            JsonSerializer.Serialize(new ControlCenterWindowSettingsStore().Load(session.SettingsPath)));
    }

    [TestMethod]
    public void DuplicateCandidateFailsBeforeDirectoryCreationAndDisposeReleasesIdentity()
    {
        var secondPath = NewSessionPath();
        var first = SampleSession.Create(TestRepository.Root, NewSessionPath(), ModulePath);
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => SampleSession.Create(TestRepository.Root, secondPath, ModulePath));
            Assert.IsFalse(Directory.Exists(secondPath));
        }
        finally { first.Dispose(); }
        Assert.IsTrue(first.IsDisposed);
        first.Dispose();
        using var next = SampleSession.Create(TestRepository.Root, secondPath, ModulePath);
        Assert.IsTrue(File.Exists(next.SettingsPath));
    }

    [TestMethod]
    public void EntryPointRejectsInvalidAndDuplicateCandidatesWithoutEscapingAsUnhandledExceptions()
    {
        var main = typeof(Dango.GaitSample.Program).GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!;
        // Both inputs must fail before WPF Application or DangoApplicationHost construction.
        Assert.AreEqual(4, main.Invoke(null, [new[] { TestRepository.Root }]));
        using var session = SampleSession.Create(TestRepository.Root, NewSessionPath(), ModulePath);
        var duplicate = NewSessionPath();
        Assert.AreEqual(4, main.Invoke(null, [new[] { duplicate }]));
        Assert.IsFalse(Directory.Exists(duplicate));
    }

    [TestMethod]
    public void DirectoryCreationFailureReleasesCandidateIdentityAndPreservesExistingFile()
    {
        var container = NewSessionPath();
        Directory.CreateDirectory(container);
        var existingFile = Path.Combine(container, "fictional-file-parent");
        File.WriteAllText(existingFile, "preserve fictional parent");
        var invalidDirectory = Path.Combine(existingFile, "new-session");
        Assert.Throws<IOException>(() => SampleSession.Create(TestRepository.Root, invalidDirectory, ModulePath));
        Assert.AreEqual("preserve fictional parent", File.ReadAllText(existingFile));
        using var next = SampleSession.Create(TestRepository.Root, NewSessionPath(), ModulePath);
        Assert.IsTrue(File.Exists(next.SettingsPath));
    }
}
