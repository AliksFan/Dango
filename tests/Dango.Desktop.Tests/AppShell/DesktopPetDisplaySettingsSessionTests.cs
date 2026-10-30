using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetDisplaySettingsSessionTests
{
    [STATestMethod]
    public void SettingsRefreshDoesNotApplyAgainAndUserValueUsesTheSamePercentPath()
    {
        var view = new ProductHomeView();
        var requested = new List<int>();
        view.DesktopPetSizeRequested += requested.Add;
        view.UpdateDesktopPetDisplaySettings(125, null, connected: true);
        view.UpdateDesktopPetDisplaySettings(150, "当前比例未保存", connected: true);
        Assert.AreEqual(0, requested.Count, "Reading settings cannot trigger persistence or display changes.");
        var slider = (System.Windows.Controls.Slider)view.FindName("DesktopPetSizeSlider");
        Assert.AreEqual(150d, slider.Value);
        Assert.AreEqual(50d, slider.Minimum);
        Assert.AreEqual(200d, slider.Maximum);
        slider.Value = 175;
        CollectionAssert.AreEqual(new[] { 175 }, requested);
        Assert.AreEqual("175%", ((System.Windows.Controls.TextBlock)view.FindName("DesktopPetSizeValue")).Text);
        Assert.IsNull(System.Windows.PresentationSource.FromVisual(view), "This regression check must never create a window.");
    }

    [TestMethod]
    public void MissingPreferenceDefaultsToOneHundredWithoutWritingAndExplicitSaveRoundTrips()
    {
        var path = NewPath();
        var session = new DesktopPetDisplaySettingsSession(path);
        Assert.AreEqual(100, session.Percent);
        Assert.IsFalse(session.IsWriteProtected);
        Assert.IsFalse(File.Exists(path));
        session.SetPercent(175);
        Assert.AreEqual(100, session.SavedPercent);
        Assert.IsTrue(session.HasPendingSave);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(175, session.SavedPercent);
        Assert.IsFalse(session.HasPendingSave);
        Assert.IsNull(session.StatusMessage);
        Assert.AreEqual(175, new DesktopPetDisplaySettingsSession(path).Percent);
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
    }

    [TestMethod]
    public void FailedAtomicSaveKeepsTheActivePercentAndOldFileThenAllowsRetry()
    {
        var path = NewPath();
        var seed = new DesktopPetDisplaySettingsSession(path);
        seed.SetPercent(80);
        Assert.IsTrue(seed.TrySave());
        var original = File.ReadAllBytes(path);
        var writer = new ToggleWriter { Fail = true };
        var session = new DesktopPetDisplaySettingsSession(path, writer);
        session.SetPercent(150);
        Assert.IsFalse(session.TrySave());
        Assert.AreEqual(150, session.Percent);
        Assert.AreEqual(80, session.SavedPercent);
        Assert.IsTrue(session.HasPendingSave);
        Assert.IsNotNull(session.StatusMessage);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(150, new DesktopPetDisplaySettingsSession(path).Percent);
        Assert.IsNull(session.StatusMessage);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("{\"schemaVersion\":2,\"percent\":100}")]
    [DataRow("{\"schemaVersion\":1,\"percent\":49}")]
    [DataRow("{\"schemaVersion\":1,\"percent\":201}")]
    [DataRow("{\"schemaVersion\":1,\"percent\":100.5}")]
    [DataRow("{\"schemaVersion\":1,\"percent\":100,\"unknown\":true}")]
    [DataRow("{\"schemaVersion\":1,\"percent\":100,\"percent\":150}")]
    [DataRow("{\"schemaVersion\":1}")]
    public void InvalidOrUnknownPreferenceProtectsTheExactOriginal(string json)
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        var original = File.ReadAllBytes(path);
        var writer = new ToggleWriter();
        var session = new DesktopPetDisplaySettingsSession(path, writer);
        Assert.IsTrue(session.IsWriteProtected);
        Assert.AreEqual(100, session.Percent);
        Assert.IsNotNull(session.StatusMessage);
        session.SetPercent(125);
        Assert.IsFalse(session.TrySave());
        Assert.AreEqual(125, session.Percent, "Protecting storage must not misreport the active display choice.");
        Assert.AreEqual(0, writer.Writes);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void RangeIsValidatedIndependentlyOfTheSlider()
    {
        var session = new DesktopPetDisplaySettingsSession(NewPath());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetPercent(49));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetPercent(201));
        Assert.AreEqual(100, session.Percent);
        session.SetPercent(50);
        Assert.IsTrue(session.TrySave());
        session.SetPercent(200);
        Assert.IsTrue(session.TrySave());
    }

    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "desktop-pet-display-tests",
        Guid.NewGuid().ToString("N"), DesktopPetDisplaySettingsSession.FileName);

    private sealed class ToggleWriter : IAtomicTextWriter
    {
        internal bool Fail;
        internal int Writes;
        public void Write(string path, string content)
        {
            Writes++;
            if (Fail) throw new IOException("Fictional preference storage failure.");
            new AtomicTextFileWriter().Write(path, content);
        }
    }
}
