using System.Reflection;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Accessories;

[TestClass, DoNotParallelize]
public sealed class AccessoryDesktopHandoffTests
{
    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void RoomEntryUsesAppliedWardrobeAndNeverAnUncommittedDraft(bool removeAll, bool cancelDraft)
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "accessory-desktop-handoff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"));
        var window = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        Assert.IsFalse(window.IsVisible, "This regression must never show or activate the control center.");
        var home = window.ProductHome;
        Assert.IsNull(home.BoundRuntime, "The continuous homepage must exercise its separate body runtime.");
        var session = home.BoundAccessories!;
        var configuration = removeAll ? AccessoryConfiguration.Empty : new AccessoryConfiguration([
            new("fictional-bowler", "Fictional saved accessory", "builtin-bowler", 64, 64)
            {
                BodyAnchorX = .23, BodyAnchorY = .31, BaseAngleDegrees = 17,
                DeformationMode = AccessoryDeformationMode.SingleChain,
                Rig = new([new(.5, .5), new(.7, .8)], .7), GravityInfluence = 31,
            },
        ]);
        session.Stage(configuration);
        Field<AccessoryEditorPanel>(home, "accessoryPanel").ApplyDraft();
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(configuration, new AccessorySettingsSession(root).SavedConfiguration,
            "The real editor Apply action must have committed the complete local configuration.");
        session.Stage(new AccessoryConfiguration([
            new("fictional-uncommitted", "Fictional uncommitted accessory", "builtin-top-hat", 64, 64),
        ]));
        if (cancelDraft) home.CancelAccessoryPreview();
        PrepareRoom(host);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom);
        Assert.IsTrue(runtime.AccessoriesEnabled,
            "A saved empty or customized list must replace legacy-hat compatibility in the room runtime.");
        var controller = Field<AccessorySceneController>(runtime, "accessoryScene");
        Assert.AreEqual(configuration, Field<AccessoryConfiguration>(controller, "configuration"),
            "The runtime consumed by ordinary and desktop-pet rooms must receive the saved list, not its startup sprout.");
        Assert.IsFalse(window.IsVisible);
        Assert.AreEqual(0, runtime.EntityIds.Count, "Saving must not initialize a room physics world.");
    }

    [STATestMethod]
    public void RoomEntryPreservesLegacyHatCompatibilityUntilTheFirstApply()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "accessory-desktop-handoff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"));
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        var session = Field<AccessorySettingsSession>(host, "accessories");
        Assert.IsTrue(session.IsCompatibilityMode);
        session.Stage(AccessoryConfiguration.Empty);
        PrepareRoom(host);
        Assert.IsFalse(runtime.AccessoriesEnabled, "An uncommitted empty list cannot replace the legacy hat.");
        var controller = Field<AccessorySceneController>(runtime, "accessoryScene");
        Assert.AreEqual(session.SavedConfiguration, Field<AccessoryConfiguration>(controller, "configuration"));
        Assert.IsFalse(File.Exists(Path.Combine(root, "accessories-v2.json")));
        Assert.AreEqual(0, runtime.EntityIds.Count);
    }

    private static void PrepareRoom(DangoApplicationHost host) => typeof(DangoApplicationHost)
        .GetMethod("PrepareBeltRoomEntry", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
