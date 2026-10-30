using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Button = System.Windows.Controls.Button;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryUnifiedPanelTests
{
    private static string NewRoot() => Path.Combine(TestRepository.Root, "artifacts", "accessory-unified-panel", Guid.NewGuid().ToString("N"));

    [STATestMethod]
    public void CompleteModalResultStagesAtomicallyAndOnlyOuterApplyPersists()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var saved = session.SavedConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.UnifiedEditor = (ticket, source, project, _) =>
        {
            Assert.IsNotNull(source); Assert.IsNull(project);
            return Complete(ticket, ticket.Original with { SizePercent = 173, GravityInfluence = 37 });
        };
        panel.EditSelectedImage();
        Assert.AreEqual(173, panel.SelectedItem!.SizePercent);
        Assert.AreEqual(37, panel.SelectedItem.GravityInfluence);
        Assert.IsNotNull(panel.SelectedItem.ProjectId);
        Assert.IsNotNull(session.TryGetProject(panel.SelectedItem.Id));
        Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
        panel.ApplyDraft();
        Assert.AreEqual(session.DraftConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
    }

    [STATestMethod]
    public void ReopeningUsesEditableProjectAndCancelPreservesFullOuterDraft()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.UnifiedEditor = (ticket, _, _, _) => Complete(ticket, ticket.Original with { BaseAngleDegrees = 29 });
        panel.EditSelectedImage(); panel.ApplyDraft();
        var reopened = new AccessorySettingsSession(root); panel.Bind(reopened);
        var before = reopened.DraftConfiguration;
        panel.UnifiedEditor = (_, source, project, _) =>
        { Assert.IsNull(source); Assert.IsNotNull(project); return null; };
        panel.EditSelectedImage();
        Assert.AreEqual(before, reopened.DraftConfiguration);
    }

    [STATestMethod]
    public void OuterCancelRestoresSavedProjectRigAndEveryParameterBeforeReopening()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.UnifiedEditor = (ticket, _, _, _) => Complete(ticket, ticket.Original with
        {
            Name = "Saved fictional chain", ImagePivotX = .2, ImagePivotY = .3,
            DeformationMode = AccessoryDeformationMode.SingleChain,
            Rig = new([new(.2, .3), new(.7, .8)], .4, 8, 7),
        });
        panel.EditSelectedImage(); panel.ApplyDraft();
        var saved = session.SavedConfiguration;
        var original = panel.SelectedItem!;
        var manifest = File.ReadAllBytes(Path.Combine(root, "accessories-v2.json"));
        panel.UnifiedEditor = (ticket, _, _, _) => Complete(ticket, ticket.Original with
        {
            Name = "Discarded fictional chain", Layer = AccessoryLayer.Behind,
            SizePercent = 173, BaseAngleDegrees = 29, BodyAnchorX = .46, BodyAnchorY = .08,
            ImagePivotX = .3, ImagePivotY = .4, ConnectionStrength = 73, MaxConnectionTravelPercent = 37,
            LockRotation = false, RotationDamping = 14, ReturnStrength = 21,
            LimitSwing = false, MaxSwingDegrees = 17, GravityInfluence = 42,
            Rig = new([new(.3, .4), new(.6, .7)], .8, 3, 11),
        });
        panel.EditSelectedImage();
        Assert.AreNotEqual(original, panel.SelectedItem);
        Assert.AreNotEqual(original.ProjectId, panel.SelectedItem!.ProjectId);
        panel.CancelDraft();
        Assert.AreEqual(saved, session.DraftConfiguration);
        CollectionAssert.AreEqual(manifest, File.ReadAllBytes(Path.Combine(root, "accessories-v2.json")));
        Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
        panel.UnifiedEditor = (ticket, source, project, _) =>
        {
            Assert.AreEqual(original, ticket.Original);
            Assert.IsNull(source); Assert.IsNotNull(project);
            Assert.AreEqual(original.ProjectId, project.ProjectId);
            Assert.AreEqual(original.AssetId, project.FinalAssetId);
            return null;
        };
        panel.EditSelectedImage();
        Assert.AreEqual(original, panel.SelectedItem);
    }

    [STATestMethod]
    public void ReopeningCompletedItemWithSharedPendingSourcePassesOnlyItsEditableProject()
    {
        var root = NewRoot(); Directory.CreateDirectory(root);
        var session = new AccessorySettingsSession(Path.Combine(root, "settings"));
        var original = session.DraftConfiguration.Items[0];
        session.Stage(new([original]));
        var ticket = session.BeginEdit(original.Id);
        var completed = Complete(ticket, original);
        Assert.IsTrue(session.TryCompleteEdit(ticket, completed));
        var path = Path.Combine(root, "fictional-shared.png");
        File.WriteAllBytes(path, completed.Image.CopyPngBytes());
        var pending = session.ImportSource(path);
        Assert.AreEqual(completed.Item.AssetId, pending.AssetId, "Both items must share the exact normalized PNG asset.");
        Assert.IsTrue(pending.IsPendingCompletion);
        Assert.IsFalse(session.DraftConfiguration.Items.Single(item => item.Id == original.Id).IsPendingCompletion);
        Assert.IsNotNull(session.GetPendingSource(completed.Item.AssetId));
        var before = session.DraftConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectItem(original.Id);
        var opened = false;
        panel.UnifiedEditor = (draft, source, project, _) =>
        {
            opened = true;
            Assert.AreEqual(original.Id, draft.ItemId);
            Assert.IsNull(source, "A completed item must not receive another item's pending source.");
            Assert.IsNotNull(project);
            Assert.AreEqual(completed.Project.ProjectId, project.ProjectId);
            return null;
        };
        panel.EditSelectedImage();
        Assert.IsTrue(opened); Assert.AreEqual(before, session.DraftConfiguration);
    }

    [STATestMethod]
    public void MissingSavedProjectCannotSilentlyOpenFlattenedImage()
    {
        var session = new AccessorySettingsSession(NewRoot());
        var item = session.DraftConfiguration.Items[0] with { ProjectId = new string('a', 64), ProjectVersion = AccessoryEditorProject.CurrentVersion };
        session.Stage(new([item]));
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var opened = false;
        panel.UnifiedEditor = (_, _, _, _) => { opened = true; return null; };
        panel.EditSelectedImage();
        Assert.IsFalse(opened); Assert.AreEqual(item, panel.SelectedItem);
        StringAssert.Contains(panel.StatusMessage, "工程已隔离");
    }

    [STATestMethod]
    public void CreateOpensEmptyProjectImmediatelyAndCancelRetainsOnlyAnUnsavedPendingDraft()
    {
        var settingsRoot = NewRoot();
        var session = new AccessorySettingsSession(settingsRoot); var saved = session.SavedConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var opened = 0;
        panel.UnifiedEditor = (ticket, source, project, _) =>
        {
            opened++; Assert.IsTrue(ticket.Original.IsPendingCompletion);
            Assert.IsNull(source); Assert.IsNotNull(project);
            Assert.AreEqual(512, project.PixelWidth); Assert.AreEqual(512, project.PixelHeight);
            Assert.AreEqual(1, project.Layers.Count); Assert.IsNull(project.Layers[0].Type);
            Assert.AreEqual(0, project.Assets.Count);
            var payload = new AccessoryBrowserProjectAdapter().CreateOpenProject(project);
            Assert.AreEqual(0, payload.GetProperty("assets").GetArrayLength());
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, payload.GetProperty("layers")[0].GetProperty("type").ValueKind);
            Assert.AreEqual(ticket.Original, panel.SelectedItem);
            return null;
        };
        ((StackPanel)panel.ActionTools).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(1, opened); Assert.IsTrue(panel.SelectedItem!.IsPendingCompletion);
        Assert.AreEqual(AccessoryEditorSection.Placement, panel.SelectedSection);
        Assert.IsNotNull(session.TryGetProject(panel.SelectedItem.Id));
        Assert.IsNotNull(session.GetPendingSource(panel.SelectedItem.AssetId));
        StringAssert.Contains(panel.StatusMessage, "待完成");
        panel.EditSelectedImage(); Assert.AreEqual(2, opened);
        panel.ApplyDraft(); Assert.AreEqual(saved, session.SavedConfiguration);
        Assert.IsFalse(Directory.Exists(settingsRoot), "An incomplete blank project must not create persistent files.");
        panel.CancelDraft(); Assert.AreEqual(saved, session.DraftConfiguration);
    }

    [STATestMethod]
    public void CompletedBlankProjectRemainsDraftUntilApplyAndReloadsWithItsProject()
    {
        var settingsRoot = NewRoot(); var session = new AccessorySettingsSession(settingsRoot);
        var saved = session.SavedConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.UnifiedEditor = (ticket, source, project, _) =>
        {
            Assert.IsNull(source); Assert.IsNotNull(project);
            return Complete(ticket, ticket.Original with { Name = "Fictional drawing" });
        };
        ((StackPanel)panel.ActionTools).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var completed = panel.SelectedItem!;
        Assert.IsFalse(completed.IsPendingCompletion);
        Assert.IsNotNull(session.TryGetProject(completed.Id));
        Assert.AreEqual(saved, new AccessorySettingsSession(settingsRoot).SavedConfiguration);
        panel.ApplyDraft();
        var reopened = new AccessorySettingsSession(settingsRoot);
        Assert.AreEqual(session.DraftConfiguration, reopened.SavedConfiguration);
        Assert.IsNotNull(reopened.TryGetProject(completed.Id));
    }

    [STATestMethod]
    public void TemporaryPreviewUsesActualImageWithoutStagingAndRejectsCallbackAfterModalEnds()
    {
        var session = new AccessorySettingsSession(NewRoot()); var before = session.DraftConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        Action<AccessoryEditResult>? late = null; AccessoryEditResult? snapshot = null;
        panel.UnifiedEditor = (ticket, _, _, preview) =>
        {
            snapshot = Complete(ticket, ticket.Original with { GravityInfluence = 42 }); late = preview;
            preview(snapshot);
            Assert.AreSame(snapshot.Image.Bitmap, panel.ResolvePreviewImage(snapshot.Item.AssetId));
            Assert.AreEqual(before, session.DraftConfiguration);
            return null;
        };
        panel.EditSelectedImage();
        Assert.IsNull(panel.ModalPreviewResult); Assert.AreEqual(before, session.DraftConfiguration);
        late!(snapshot!); Assert.IsNull(panel.ModalPreviewResult);
    }

    [STATestMethod]
    public void RebindAndOuterCancellationRejectLateCompleteResult()
    {
        foreach (var rebind in new[] { false, true })
        {
            var session = new AccessorySettingsSession(NewRoot()); var replacement = new AccessorySettingsSession(NewRoot());
            var panel = new AccessoryEditorPanel(); panel.Bind(session);
            panel.UnifiedEditor = (ticket, _, _, _) =>
            {
                if (rebind) panel.Bind(replacement); else panel.CancelDraft();
                return Complete(ticket, ticket.Original with { SizePercent = 350 });
            };
            panel.EditSelectedImage();
            Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
            Assert.AreEqual(replacement.SavedConfiguration, replacement.DraftConfiguration);
        }
    }

    [STATestMethod]
    public void ForeignItemResultIsRejectedWithoutPartialAssetOrParameterChange()
    {
        var session = new AccessorySettingsSession(NewRoot()); var before = session.DraftConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.UnifiedEditor = (ticket, _, _, _) => Complete(ticket, ticket.Original with { Id = "fictional-other", SizePercent = 350 });
        panel.EditSelectedImage();
        Assert.AreEqual(before, session.DraftConfiguration);
        StringAssert.Contains(panel.StatusMessage, "已过期");
    }

    private static AccessoryEditResult Complete(AccessoryEditDraft ticket, AccessoryItem item)
    {
        var source = AccessoryEditorSourceAsset.FromBitmap(BuiltinAccessoryImages.Get("builtin-sprout")!);
        var image = AccessoryImageAsset.FromRenderedPng(source.CopyPngBytes());
        var project = AccessoryEditorProject.FromImage(image, ticket.Revision);
        return new(project, image, item with { AssetId = image.AssetId, PixelWidth = image.PixelWidth, PixelHeight = image.PixelHeight,
            ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = ticket.Revision, IsPendingCompletion = false });
    }
}
