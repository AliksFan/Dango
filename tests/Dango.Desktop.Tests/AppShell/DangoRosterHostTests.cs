using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Dangos;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

/// <summary>Formal product composition without showing a window or faking a room admission.</summary>
[TestClass]
public sealed class DangoRosterHostTests
{
    [STATestMethod]
    public void BoundEditorsSaveOnlySelectedIdentityAndLeaveLegacySourcesUnchanged()
    {
        using var data = new DataDirectory();
        var oldAppearance = Path.Combine(data.Root, "appearance.json");
        new AppearanceSettingsStore().Save(oldAppearance, DangoAppearanceConfiguration.Default);
        new AccessorySettingsStore().Save(data.Root, AccessoryConfiguration.FromLegacyHat("sprout"));
        var appearanceBytes = File.ReadAllBytes(oldAppearance);
        var accessoryBytes = File.ReadAllBytes(Path.Combine(data.Root, "accessories.json"));
        var store = new DangoRosterStore(data.Root);
        var snapshot = store.LoadOrInitialize().Snapshot!;
        var a = snapshot.SelectedId!.Value;
        snapshot = Commit(store.Rename(snapshot, a, "小栗", Guid.NewGuid()));
        snapshot = Commit(store.Create(snapshot, 2, "小蓝", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        var b = snapshot.SelectedId!.Value;
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        var home = Home(host);
        var appearance = home.BoundAppearance!;
        // This is the editor persistence boundary; a synthetic acknowledgement is not a visual acceptance test.
        Assert.IsTrue(appearance.MarkBodyApplied(appearance.DraftAppearance.Body,
            appearance.DraftAppearance.BodyPhysicsModel, appearance.BodyConfigurationRevision, appearance.BodyEpoch));
        appearance.StageBodyPreview(OpaqueSrgbColor.Parse("#80A0D0"));
        Assert.IsTrue(appearance.TrySave(), appearance.StatusMessage);
        var accessories = home.BoundAccessories!;
        accessories.Stage(new(accessories.SavedConfiguration.Items.Select(item => item with
        {
            IsEquipped = item.AssetId == "builtin-top-hat",
            SizePercent = item.AssetId == "builtin-top-hat" ? 130 : item.SizePercent,
        })));
        Assert.IsTrue(accessories.TrySave(), accessories.StatusMessage);
        var reloaded = store.LoadOrInitialize().Snapshot!;
        Assert.AreEqual(a, reloaded.Slots[0]);
        Assert.AreEqual(b, reloaded.Slots[2]);
        Assert.AreEqual(DangoAppearanceConfiguration.Default, reloaded.Find(a)!.Appearance);
        Assert.AreEqual("#80A0D0", reloaded.Find(b)!.Appearance.BodyColor.ToHex());
        Assert.IsTrue(reloaded.EffectiveAccessories(a).Items.Single(i => i.AssetId == "builtin-sprout").IsEquipped);
        Assert.IsTrue(reloaded.EffectiveAccessories(b).Items.Single(i => i.AssetId == "builtin-top-hat").IsEquipped);
        CollectionAssert.AreEqual(appearanceBytes, File.ReadAllBytes(oldAppearance));
        CollectionAssert.AreEqual(accessoryBytes, File.ReadAllBytes(Path.Combine(data.Root, "accessories.json")));
    }

    [STATestMethod]
    public void PermanentDeletionLeavesGapAndClearsSelectionWithoutTouchingOtherIdentities()
    {
        using var data = new DataDirectory();
        var store = new DangoRosterStore(data.Root);
        var s = store.LoadOrInitialize().Snapshot!;
        s = Commit(store.Create(s, 0, "小栗", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        var a = s.SelectedId!.Value;
        s = Commit(store.Create(s, 2, "小圆", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        var c = s.SelectedId!.Value;
        s = Commit(store.Create(s, 1, "小蓝", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        var b = s.SelectedId!.Value;
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        _ = Home(host);
        Call(host, "OnDangoDeleteRequested", b);
        var saved = store.LoadOrInitialize().Snapshot!;
        Assert.AreEqual(a, saved.Slots[0]);
        Assert.IsNull(saved.Slots[1]);
        Assert.AreEqual(c, saved.Slots[2]);
        Assert.IsNull(saved.SelectedId);
        Assert.IsNull(saved.Find(b));
        Assert.IsNull(host.ActiveDangoId);
    }

    [STATestMethod]
    public void AwaySceneRejectsRenameDeleteAndSelectionCommandsAtTheHostBoundary()
    {
        using var data = new DataDirectory();
        var store = new DangoRosterStore(data.Root);
        var s = store.LoadOrInitialize().Snapshot!;
        s = Commit(store.Create(s, 0, "小栗", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        s = Commit(store.Create(s, 1, "小蓝", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        var b = s.SelectedId!.Value;
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        Home(host).SetDangoAway(true);
        Call(host, "OnDangoRenameRequested", b, "不应保存");
        Call(host, "OnDangoDeleteRequested", b);
        Call(host, "OnDangoSlotRequested", 0);
        var saved = store.LoadOrInitialize().Snapshot!;
        Assert.AreEqual(s.Revision, saved.Revision);
        Assert.AreEqual("小蓝", saved.Find(b)!.Name);
        Assert.AreEqual(b, saved.SelectedId);
    }

    [STATestMethod]
    public void LegacyUnnamedIdentityIsOccupiedAndClearlyRequestsAName()
    {
        using var data = new DataDirectory();
        new AppearanceSettingsStore().Save(Path.Combine(data.Root, "appearance.json"), DangoAppearanceConfiguration.Default);
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        var home = Home(host);
        var id = host.DangoRoster!.SelectedId;
        Assert.IsNotNull(id);
        Assert.AreEqual(id, host.DangoRoster.Slots[0]);
        Assert.IsNull(host.DangoRoster.Find(id.Value)!.Name);
        Assert.AreEqual("请为团子起名", ((System.Windows.Controls.Button)home.FindName("DangoNameButton")).Content);
        Assert.AreEqual("1", ((System.Windows.Controls.Button)home.FindName("DangoSlot1")).Content);
    }

    [STATestMethod]
    public void RebindingTheSameIdentityInvalidatesItsPreviousEditorSaveTarget()
    {
        using var data = new DataDirectory();
        var store = new DangoRosterStore(data.Root);
        var s = store.LoadOrInitialize().Snapshot!;
        s = Commit(store.Create(s, 0, "小栗", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        var home = Home(host);
        var stale = home.BoundAppearance!;
        Assert.IsTrue(stale.MarkBodyApplied(stale.DraftAppearance.Body, stale.DraftAppearance.BodyPhysicsModel,
            stale.BodyConfigurationRevision, stale.BodyEpoch));
        Call(host, "RestoreSelectedDangoSessions");
        Assert.AreNotSame(stale, home.BoundAppearance);
        stale.StageBodyPreview(OpaqueSrgbColor.Parse("#80A0D0"));
        Assert.IsFalse(stale.TrySave(), "A delayed editor must not write even when its identity is selected again.");
        Assert.AreEqual(s.Revision, store.LoadOrInitialize().Snapshot!.Revision);
    }

    [STATestMethod]
    public void MissingAccountCannotChangeDisplaySourceOrCreateAnAccount()
    {
        using var data = new DataDirectory();
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        _ = Home(host);
        var before = host.DangoRoster!;
        Call(host, "OnOrdinaryRoomNameSourceRequested", true);
        Assert.AreEqual(OrdinaryRoomNameSource.DangoName, host.DangoRoster!.NameSource);
        Assert.AreEqual(before.Revision, host.DangoRoster.Revision);
        Assert.AreEqual(0, host.DangoRoster.Profiles.Count);
        Assert.IsFalse(File.Exists(Path.Combine(data.Root, "appearance.json")));
        Assert.IsFalse(File.Exists(Path.Combine(data.Root, "accessories.json")));
    }

    private static ProductHomeView Home(DangoApplicationHost host)
    {
        var home = ((ControlCenterWindow)Call(host, "GetOrCreateControlCenter")!).ProductHome;
        ProductHostTestSupport.WaitFor(() => home.CatalogSnapshot is not null,
            "The actual catalog IO must finish before the fixture can own and clean up its data root.");
        return home;
    }

    private static object? Call(DangoApplicationHost host, string name, params object[] args) =>
        typeof(DangoApplicationHost).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, args);

    private static DangoRosterSnapshot Commit(DangoRosterWriteResult result)
    {
        Assert.AreEqual(FurnitureCommitState.Committed, result.State, result.Warning);
        return result.Snapshot!;
    }

    private sealed class DataDirectory : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Dango-three-slots-" + Guid.NewGuid().ToString("N"));
        internal string SettingsPath => Path.Combine(Root, "settings.json");
        internal DataDirectory() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var parent = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("Dango-three-slots-", StringComparison.Ordinal))
                throw new InvalidOperationException("临时测试目录边界无效。");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }
}
