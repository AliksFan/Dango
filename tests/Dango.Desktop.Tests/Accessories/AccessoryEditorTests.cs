using Dango.Desktop.Accessories.Editing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorTests
{
    private static string NewRoot() => Path.Combine(TestRepository.Root, "artifacts", "accessory-ui-editor-harness", "editor-data", Guid.NewGuid().ToString("N"));

    [STATestMethod]
    public void UnchangedPreviewErrorStillReportsANewImageLoadWarning()
    {
        var session = new AccessorySettingsSession(NewRoot());
        var unavailable = new AccessoryItem("fictional-missing-status", "Fictional missing status", new string('a', 64), 64, 64);
        session.Stage(new([unavailable]));
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.SetPreviewError(null);
        Assert.IsFalse(panel.StatusMessage.Contains("部分配饰图片无法显示", StringComparison.Ordinal));
        Assert.IsNull(session.TryGetImage(unavailable.AssetId));
        panel.SetPreviewError(null);
        StringAssert.Contains(panel.StatusMessage, "部分配饰图片无法显示");
    }

    [STATestMethod]
    public void LayerActionsAreFourFaceIconsAndParameterSectionsHaveNoVisibleHeadings()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var item = new AccessoryItem("fictional-layer-icons", "Fictional layer icons", "builtin-sprout", 64, 64);
        session.Stage(new([item])); Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.SelectSection(AccessoryEditorSection.Placement);
        WithWindow(panel, 280, 1000, () =>
        {
            var names = new[] { "团子前", "团子后", "上移", "下移" };
            Assert.IsFalse(Descendants(panel).OfType<ComboBox>().Any(), "Layer selection must use reachable icons instead of a dropdown.");
            var buttons = Descendants(panel).OfType<Button>().Where(button => names.Contains(AutomationProperties.GetName(button))).ToArray();
            CollectionAssert.AreEqual(names, buttons.Select(AutomationProperties.GetName).ToArray());
            AssertFaceIcons(buttons);
            foreach (var button in buttons) Assert.IsTrue(button.IsVisible && button.IsEnabled && button.ToolTip is string);
            var before = buttons[0]; var behind = buttons[1];
            Assert.AreEqual("已选", AutomationProperties.GetItemStatus(before));
            Assert.AreEqual("未选", AutomationProperties.GetItemStatus(behind));
            ((IInvokeProvider)new ButtonAutomationPeer(behind).GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
            Assert.AreEqual(AccessoryLayer.Behind, panel.SelectedItem!.Layer);
            Assert.AreEqual("已选", AutomationProperties.GetItemStatus(behind));
            Assert.AreEqual("未选", AutomationProperties.GetItemStatus(before));
            Assert.AreEqual(item, new AccessorySettingsSession(root).SavedConfiguration.Items.Single(), "Changing the layer must remain a draft until Apply.");
            panel.ApplyDraft();
            Assert.AreEqual(AccessoryLayer.Behind, new AccessorySettingsSession(root).SavedConfiguration.Items.Single().Layer);
            foreach (var section in new[] { AccessoryEditorSection.Placement })
            {
                Assert.IsTrue(panel.SelectSection(section)); Pump();
                Assert.IsFalse(Descendants(panel).OfType<TextBlock>().Any(text => text.IsVisible && text.Text is "图像与位置" or "柔性与连接" or "旋转与重力" or "图层"),
                    "Icon navigation already identifies the section; repeated visible headings must be omitted.");
            }
        });
    }

    [STATestMethod]
    public void ImageConfirmationOnlyStagesAndExplicitApplySurvivesReload()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var saved = session.SavedConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var previews = new List<AccessoryConfiguration>(); var applyCount = 0;
        panel.PreviewChanged += configuration => previews.Add(configuration); panel.Applied += _ => applyCount++;
        panel.UnifiedEditor = (draft, _, _, _) => Complete(draft, draft.Original with { SizePercent = 185, BaseAngleDegrees = -45, ImagePivotX = .18, ImagePivotY = .72 });
        panel.EditSelectedImage();

        Assert.AreEqual(185, panel.SelectedItem!.SizePercent);
        Assert.AreEqual(-45, panel.SelectedItem.BaseAngleDegrees);
        Assert.AreEqual(.18, panel.SelectedItem.ImagePivotX);
        Assert.AreEqual(saved, session.SavedConfiguration);
        Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
        Assert.IsTrue(previews.Count > 0);
        foreach (var preview in previews) Assert.AreEqual(session.DraftConfiguration, preview, "Confirmation and modal cleanup must both project the complete staged result.");
        Assert.IsNull(panel.ModalPreviewResult); Assert.AreEqual(0, applyCount);

        panel.ApplyDraft();
        Assert.AreEqual(1, applyCount);
        Assert.AreEqual(session.DraftConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
        Assert.IsFalse(session.IsDirty);
    }

    [STATestMethod]
    public void ModalCancelKeepsPriorWardrobeDraftAndLateConfirmationCannotReviveCancelledDraft()
    {
        var session = new AccessorySettingsSession(NewRoot()); var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.SetBodyAnchor(new(.2, .6)); var priorDraft = session.DraftConfiguration;
        panel.UnifiedEditor = (_, _, _, _) => null;
        panel.EditSelectedImage();
        Assert.AreEqual(priorDraft, session.DraftConfiguration);

        panel.UnifiedEditor = (draft, _, _, _) =>
        {
            panel.CancelDraft();
            return Complete(draft, draft.Original with { SizePercent = 340 });
        };
        panel.EditSelectedImage();
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        Assert.IsFalse(session.IsDirty);
    }

    [STATestMethod]
    public void RebindingWhileUnifiedEditorIsOpenCannotWriteResultIntoAnotherSession()
    {
        var original = new AccessorySettingsSession(NewRoot());
        var replacement = new AccessorySettingsSession(NewRoot(), "bowler");
        var panel = new AccessoryEditorPanel(); panel.Bind(original);
        panel.UnifiedEditor = (draft, _, _, _) => { panel.Bind(replacement); return Complete(draft, draft.Original with { SizePercent = 350 }); };
        panel.EditSelectedImage();
        Assert.AreEqual(replacement.SavedConfiguration, replacement.DraftConfiguration);
        Assert.AreEqual(original.SavedConfiguration, original.DraftConfiguration);
    }

    [STATestMethod]
    public void EditingAndOrderingOneLayerPreservesOtherInstancesAndLayers()
    {
        var session = new AccessorySettingsSession(NewRoot());
        var a = new AccessoryItem("a", "A", "builtin-sprout", 64, 64) { Layer = AccessoryLayer.Front, Order = 4 };
        var b = a with { Id = "b", Name = "B", Order = 12 };
        var back = a with { Id = "back", Name = "Behind", Layer = AccessoryLayer.Behind, Order = 2 };
        session.Stage(new([a, b, back]));
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectItem("a");
        panel.SetBodyAnchor(new(.12, .91));
        panel.MoveSelected(1);

        Assert.AreEqual(.12, session.DraftConfiguration.Items.Single(item => item.Id == "a").BodyAnchorX);
        Assert.AreEqual(.91, session.DraftConfiguration.Items.Single(item => item.Id == "a").BodyAnchorY);
        Assert.AreEqual(b with { Order = 0 }, session.DraftConfiguration.Items.Single(item => item.Id == "b"));
        Assert.AreEqual(back, session.DraftConfiguration.Items.Single(item => item.Id == "back"));
        Assert.IsTrue(session.DraftConfiguration.Items.Single(item => item.Id == "a").Order > session.DraftConfiguration.Items.Single(item => item.Id == "b").Order);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => panel.SetBodyAnchor(new(double.NaN, .5)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => panel.SetBodyAnchor(new(1.1, .5)));
    }

    [STATestMethod]
    public void CompactPanelCanMeasureWithinTheApprovedNarrowWidth()
    {
        var panel = new AccessoryEditorPanel(); panel.Bind(new AccessorySettingsSession(NewRoot()));
        panel.Measure(new Size(125, double.PositiveInfinity));
        Assert.IsTrue(panel.DesiredSize.Width <= 125.001, $"Controls expanded the compact panel to {panel.DesiredSize.Width}.");
    }

    [STATestMethod]
    public void SavedConfigurationDoesNotClearAnUnrecoveredHostPreviewError()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        panel.Edit(item => item with { SizePercent = 140 });
        const string failure = "虚构配饰显示失败；配置仍保留。";
        panel.SetPreviewError(failure); panel.ApplyDraft();
        Assert.IsFalse(session.IsDirty);
        Assert.AreEqual(session.DraftConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
        StringAssert.Contains(panel.StatusMessage, failure);
        StringAssert.Contains(panel.StatusMessage, "已保存");
        panel.Refresh(); StringAssert.Contains(panel.StatusMessage, failure);
        panel.SetPreviewError(null);
        Assert.IsFalse(panel.StatusMessage.Contains(failure, StringComparison.Ordinal));
        StringAssert.Contains(panel.StatusMessage, "已保存");
    }

    [STATestMethod]
    public void FooterMatchesFaceIconsAndOnlyChangedPresentationNotifies()
    {
        var session = new AccessorySettingsSession(NewRoot()); Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        WithWindow(panel, 220, 640, () =>
        {
            var actions = Descendants(panel.SaveActions).OfType<Button>().ToArray();
            CollectionAssert.AreEqual(new[] { "取消", "应用" }, actions.Select(AutomationProperties.GetName).ToArray());
            AssertFaceIcons(actions);
            var apply = actions[1]; Assert.IsFalse(apply.IsEnabled); Assert.AreEqual("Primary", apply.Tag);
            Assert.AreSame(panel.FindResource("HomeMuted"), apply.Foreground);
            var status = Descendants(panel.SaveActions).OfType<TextBlock>().Single();
            Assert.AreEqual(11d, status.FontSize); Assert.AreSame(panel.FindResource("HomeMuted"), status.Foreground);
            foreach (var action in actions) Assert.IsTrue(!string.IsNullOrWhiteSpace(action.ToolTip as string));
            var notifications = 0; panel.PresentationChanged += () => notifications++;
            panel.Refresh(); panel.SetPreviewError(null); Assert.AreEqual(0, notifications);
            panel.Edit(item => item with { SizePercent = 140 });
            Assert.AreEqual(1, notifications); Assert.IsTrue(apply.IsEnabled);
            Assert.AreEqual("草稿即时预览 · 应用后保存", panel.PreviewNotice);
            panel.Refresh(); Assert.AreEqual(1, notifications);
            panel.SetPreviewError("虚构预览错误"); Assert.AreEqual(2, notifications);
            panel.SetPreviewError("虚构预览错误"); panel.Refresh(); Assert.AreEqual(2, notifications);
            panel.SetPreviewError(null); Assert.AreEqual(3, notifications);
            panel.CancelDraft(); Assert.AreEqual(4, notifications);
            Assert.IsFalse(apply.IsEnabled); Assert.IsFalse(session.IsDirty);
            Capture(panel.SaveActions, "face-aligned-save-actions");
        });
    }

    [STATestMethod]
    public void HomeStylesAndPrimaryContrastRemainLiveAfterFooterDetaches()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var session = new AccessorySettingsSession(NewRoot()); Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var host = new StackPanel { Resources = home.Resources };
        host.Children.Add(panel);
        host.Children.Add(panel.ActionTools);
        panel.Children.Remove(panel.SaveActions); host.Children.Add(panel.SaveActions);
        WithWindow(host, 260, 640, () =>
        {
            var apply = Descendants(panel.SaveActions).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "应用");
            Assert.AreSame(home.FindResource("FaceIconButton"), apply.Style);
            var import = Descendants(panel.ActionTools).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "创建新配饰");
            Assert.AreSame(home.FindResource("FaceIconButton"), import.Style);
            home.Resources["HomeInk"] = Brushes.DarkSlateBlue;
            home.Resources["HomePrimaryInk"] = Brushes.White;
            home.Resources["HomeSelected"] = Brushes.DarkBlue;
            home.Resources["HomeMuted"] = Brushes.DarkGray;
            panel.Edit(item => item with { SizePercent = 145 }); Pump();
            Assert.AreSame(Brushes.DarkSlateBlue, import.Foreground, "Local fallbacks must not freeze the Home theme.");
            Assert.AreSame(Brushes.White, apply.Foreground); Assert.AreSame(Brushes.DarkBlue, apply.Background);
            panel.CancelDraft(); Pump(); Assert.AreSame(Brushes.DarkGray, apply.Foreground);
            AssertFaceIcons(Descendants(panel.SaveActions).OfType<Button>());
        });
    }

[STATestMethod]
    public void FocusedNameInputCanApplyBeforeBlurWithoutChangingUntouchedParameters()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var precise = session.DraftConfiguration.Items[0] with { BodyAnchorX = .731234, BodyAnchorY = .287654, BaseAngleDegrees = 12.123456 };
        session.Stage(new(session.DraftConfiguration.Items.Select(item => item.Id == precise.Id ? precise : item)));
        Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement);
        WithWindow(panel, 280, 900, () =>
        {
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
            var apply = Descendants(panel.SaveActions).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "应用");
            Assert.IsFalse(apply.IsEnabled); Assert.IsTrue(input.Focus()); Pump();
            input.Text = "Fictional focused rename"; Pump(); Assert.AreSame(input, Keyboard.FocusedElement);
            Assert.IsTrue(apply.IsEnabled, "Valid buffered names must enable Apply before blur.");
            ((IInvokeProvider)new ButtonAutomationPeer(apply).GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
            var loaded = new AccessorySettingsSession(root).SavedConfiguration.Items.Single(item => item.Id == precise.Id);
            Assert.AreEqual(precise with { Name = "Fictional focused rename" }, loaded);
            Assert.IsFalse(session.IsDirty); Assert.IsFalse(apply.IsEnabled);
        });
    }

[STATestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("too-long")]
    public void ApplyRejectsInvalidBufferedNameWithoutWritingOrDiscardingText(string value)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration; var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement);
        WithWindow(panel, 280, 900, () =>
        {
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
            Assert.IsTrue(input.Focus()); Pump(); var invalid = value == "too-long" ? new string('x', 129) : value;
            input.Text = invalid; panel.ApplyDraft();
            Assert.AreEqual(saved, session.DraftConfiguration);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            Assert.AreEqual(invalid, input.Text); StringAssert.Contains(panel.StatusMessage, "配饰名称");
            panel.CancelDraft(); Assert.AreEqual(saved, session.DraftConfiguration);
            Assert.AreEqual(saved.Items[0].Name, input.Text);
        });
    }

[STATestMethod]
    [DataRow("select", true)]
    [DataRow("select", false)]
    [DataRow("duplicate", true)]
    [DataRow("duplicate", false)]
    [DataRow("image", true)]
    [DataRow("image", false)]
    public void BufferedNameStaysWithItsAccessoryAcrossDependentActions(string action, bool valid)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var a = new AccessoryItem("a", "Fictional A", "builtin-sprout", 64, 64) { BodyAnchorX = .27, BodyAnchorY = .18 };
        var b = a with { Id = "b", Name = "Fictional B", BodyAnchorX = .81, BodyAnchorY = .92 };
        session.Stage(new([a, b])); Assert.IsTrue(session.TrySave());
        var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement);
        AccessoryItem? editorInput = null;
        panel.UnifiedEditor = (draft, _, _, _) => { editorInput = draft.Original; return Complete(draft, draft.Original with { SizePercent = 185 }); };
        WithWindow(panel, 280, 900, () =>
        {
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
            Assert.IsTrue(input.Focus()); Pump(); input.Text = valid ? "Fictional buffered A" : " ";
            if (action == "select") Library(panel, true).SelectedItem = Library(panel, true).Items.Cast<ListBoxItem>().Single(row => (string)row.Tag == b.Id);
            else
            {
                var button = Descendants(action == "duplicate" ? panel.ActionTools : panel).OfType<Button>().Single(candidate =>
                    AutomationProperties.GetName(candidate) == (action == "duplicate" ? "复制配饰" : "编辑配饰"));
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
            }
            Pump(); Assert.AreEqual(b, session.DraftConfiguration.Items.Single(item => item.Id == b.Id));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            if (!valid)
            {
                Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration); Assert.AreEqual(a.Id, panel.SelectedItem!.Id);
                Assert.HasCount(2, session.DraftConfiguration.Items); Assert.IsNull(editorInput); Assert.AreEqual(" ", input.Text);
                StringAssert.Contains(panel.StatusMessage, "配饰名称"); return;
            }
            var committedA = session.DraftConfiguration.Items.Single(item => item.Id == a.Id);
            Assert.AreEqual("Fictional buffered A", committedA.Name); Assert.AreEqual(a.BodyAnchorX, committedA.BodyAnchorX);
            Assert.AreEqual(a.BodyAnchorY, committedA.BodyAnchorY);
            if (action == "select") { Assert.AreEqual(b.Id, panel.SelectedItem!.Id); Assert.AreEqual(b.Name, input.Text); }
            else if (action == "duplicate")
            {
                Assert.HasCount(3, session.DraftConfiguration.Items); Assert.AreNotEqual(a.Id, panel.SelectedItem!.Id); Assert.AreNotEqual(b.Id, panel.SelectedItem.Id);
                StringAssert.StartsWith(panel.SelectedItem.Name, "Fictional buffered A");
                panel.Edit(item => item with { Name = "Fictional changed copy" });
                Assert.AreEqual(committedA, session.DraftConfiguration.Items.Single(item => item.Id == a.Id), "Copy edits cannot mutate their source.");
            }
            else { Assert.IsNotNull(editorInput); Assert.AreEqual("Fictional buffered A", editorInput.Name); Assert.AreEqual(185d, panel.SelectedItem!.SizePercent); }
        });
    }

[STATestMethod]
    [DataRow("layer", true)]
    [DataRow("layer", false)]
    [DataRow("equipped", true)]
    [DataRow("equipped", false)]
    public void BufferedNameDoesNotOverwriteTheFollowingChoice(string choice, bool valid)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var item = new AccessoryItem("choice", "Fictional choice", "builtin-sprout", 64, 64) { BodyAnchorX = .27 };
        session.Stage(new([item])); Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement);
        WithWindow(panel, 280, 1000, () =>
        {
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
            Assert.IsTrue(input.Focus()); Pump(); input.Text = valid ? "Fictional buffered choice" : " ";
            if (choice == "layer")
            {
                var behind = Descendants(panel).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "团子后");
                ((IInvokeProvider)new ButtonAutomationPeer(behind).GetPattern(PatternInterface.Invoke)!).Invoke();
            }
            else panel.ToggleWear(item.Id);
            Pump();
            var expected = item with { Name = "Fictional buffered choice" };
            expected = choice == "layer" ? expected with { Layer = AccessoryLayer.Behind } : expected with { IsEquipped = false };
            Assert.AreEqual(valid ? expected : item, panel.SelectedItem);
            if (!valid) { Assert.AreEqual(" ", input.Text); StringAssert.Contains(panel.StatusMessage, "配饰名称"); }
            Assert.AreEqual(item, session.SavedConfiguration.Items.Single());
        });
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TwoSectionIconsValidateBufferedInputBeforeHidingItsSection(bool valid)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration;
        var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var host = new DockPanel(); DockPanel.SetDock(panel.SectionTabs, Dock.Left);
        host.Children.Add(panel.SectionTabs); host.Children.Add(panel);
        WithWindow(host, 400, 850, () =>
        {
            var tabs = Descendants(panel.SectionTabs).OfType<Button>().ToArray();
            CollectionAssert.AreEqual(new[] { "配饰：配饰库", "配饰：配饰" }, tabs.Select(AutomationProperties.GetName).ToArray());
            AssertFaceIcons(tabs); Assert.AreEqual(AccessoryEditorSection.Library, panel.SelectedSection);
            Assert.HasCount(1, tabs.Where(tab => Equals(tab.Tag, "Selected")));
            for (var i = 1; i < tabs.Length; i++)
            {
                var previous = tabs[i - 1].TransformToAncestor(panel.SectionTabs).TransformBounds(new Rect(0, 0, tabs[i - 1].ActualWidth, tabs[i - 1].ActualHeight));
                var current = tabs[i].TransformToAncestor(panel.SectionTabs).TransformBounds(new Rect(0, 0, tabs[i].ActualWidth, tabs[i].ActualHeight));
                Assert.AreEqual(previous.Left, current.Left, .01); Assert.IsTrue(current.Top >= previous.Bottom + 7.9);
            }
            ((IInvokeProvider)new ButtonAutomationPeer(tabs[1]).GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
            Assert.AreEqual(AccessoryEditorSection.Placement, panel.SelectedSection);
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
            Assert.IsTrue(input.Focus()); Pump(); input.Text = valid ? "Fictional section draft" : " ";
            var changes = 0; panel.SectionChanged += () => changes++;
            ((IInvokeProvider)new ButtonAutomationPeer(tabs[0]).GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
            Assert.AreEqual(valid ? AccessoryEditorSection.Library : AccessoryEditorSection.Placement, panel.SelectedSection);
            Assert.AreEqual(valid ? 1 : 0, changes); Assert.HasCount(1, tabs.Where(tab => Equals(tab.Tag, "Selected")));
            if (valid)
            {
                Assert.AreEqual("Fictional section draft", panel.SelectedItem!.Name);
                Assert.IsFalse(input.IsVisible);
            }
            else
            {
                Assert.AreEqual(saved, session.DraftConfiguration); Assert.IsTrue(input.IsVisible);
                Assert.AreEqual(" ", input.Text);
                StringAssert.Contains(panel.StatusMessage, "配饰名称");
            }
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        });
    }

    [STATestMethod]
    public void FourColumnLibraryLoadsTransparentVisibleThumbnailsAndReusesABoundedCache()
    {
        var root = NewRoot(); var initial = new AccessorySettingsSession(root);
        initial.Stage(AccessoryConfiguration.Empty);
        for (var i = 0; i < 48; i++) initial.ImportPng(WriteFictionalTransparentPng(root, i));
        Assert.IsTrue(initial.TrySave());
        var session = new AccessorySettingsSession(root); Assert.AreEqual(0, session.CachedImageCount);
        var panel = new AccessoryEditorPanel { Width = 240 }; panel.Bind(session);
        Assert.AreEqual(0, panel.ThumbnailDecodeCount, "Constructing or refreshing an unloaded library must not decode it.");
        WithWindow(panel, 300, 640, () =>
        {
            var list = LibraryViewport(panel);
            var grid = Descendants(Library(panel, true)).OfType<System.Windows.Controls.Primitives.UniformGrid>().Single();
            Assert.AreEqual(4, grid.Columns);
            var rows = LibraryCards(panel); Assert.HasCount(48, rows);
            var bounds = rows.Take(5).Select(row => row.TransformToAncestor(list).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight))).ToArray();
            for (var i = 1; i < 4; i++)
            {
                Assert.AreEqual(bounds[0].Top, bounds[i].Top, .01);
                Assert.IsTrue(bounds[i].Left >= bounds[i - 1].Right - .01);
            }
            Assert.IsTrue(bounds[4].Top >= bounds[0].Bottom - .01);
            Assert.IsTrue(panel.ThumbnailDecodeCount > 0 && panel.ThumbnailDecodeCount < rows.Length, "Only visible assets may be decoded.");
            var decoded = panel.ThumbnailDecodeCount;
            panel.Refresh(); Pump(); Assert.AreEqual(decoded, panel.ThumbnailDecodeCount);
            foreach (var row in rows)
            {
                var item = session.DraftConfiguration.Items.Single(item => item.Id == (string)row.Tag);
                StringAssert.Contains(AutomationProperties.GetName(row), item.Name); Assert.AreEqual(item.Name, row.ToolTip);
                Assert.AreEqual(item.IsEquipped ? "已佩戴" : "未佩戴", AutomationProperties.GetItemStatus(row));
            }
            var visible = Descendants(list).OfType<System.Windows.Controls.Image>().Where(image => image.Source is not null).ToArray();
            Assert.IsTrue(visible.Length > 0); Assert.AreEqual(visible.Length, panel.VisibleThumbnailCount);
            foreach (var image in visible)
            {
                var bitmap = (BitmapSource)image.Source;
                var dpi = VisualTreeHelper.GetDpi(panel);
                var pixelEdge = (int)Math.Ceiling(AccessoryEditorPanel.ThumbnailEdge * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
                Assert.IsTrue(bitmap.IsFrozen); Assert.AreEqual(pixelEdge, Math.Max(bitmap.PixelWidth, bitmap.PixelHeight),
                    "The thumbnail must cover the actual DPI pixel size without changing its DIP layout size.");
                Assert.IsTrue(image.ActualWidth <= AccessoryEditorPanel.ThumbnailEdge + .01 && image.ActualHeight <= AccessoryEditorPanel.ThumbnailEdge + .01,
                    "Library thumbnails must stay at icon size in the rendered card.");
                Assert.AreEqual(PixelFormats.Pbgra32, bitmap.Format);
                Assert.IsTrue(Pixels(bitmap).Where((_, index) => index % 4 == 3).Any(alpha => alpha == 0), "Thumbnails must keep transparent pixels.");
            }
            var topImage = Descendants(rows[0]).OfType<System.Windows.Controls.Image>().Single();
            var scroll = list;
            scroll.ScrollToVerticalOffset(250); Pump();
            scroll.ScrollToBottom(); Pump();
            Assert.IsNull(topImage.Source, "Offscreen cards must release their displayed thumbnail references.");
            Assert.IsTrue(panel.ThumbnailDecodeCount > AccessoryEditorPanel.MaximumThumbnailCache);
            Assert.AreEqual(AccessoryEditorPanel.MaximumThumbnailCache, panel.ThumbnailCacheCount);
            Assert.IsTrue(session.CachedImageCount <= AccessorySettingsSession.MaximumCachedImages);
            panel.SelectSection(AccessoryEditorSection.Placement); Pump(); Assert.AreEqual(0, panel.VisibleThumbnailCount);
            var afterScroll = panel.ThumbnailDecodeCount;
            panel.Edit(item => item with { SizePercent = 140 }); Pump();
            Assert.AreEqual(afterScroll, panel.ThumbnailDecodeCount, "Parameter refresh must not decode hidden library images.");
        });
    }

    [STATestMethod]
    [DataRow(3)]
    [DataRow(4)]
    public void SmallLibrariesKeepFourNaturalHeightCardsPerRow(int count)
    {
        var session = new AccessorySettingsSession(NewRoot());
        session.Stage(new(Enumerable.Range(0, count).Select(index =>
            new AccessoryItem("fictional-natural-card-" + index, "Fictional natural card " + index, "builtin-sprout", 64, 64)
            { IsEquipped = true })));
        var panel = new AccessoryEditorPanel { Width = 240 }; panel.Bind(session);
        WithWindow(panel, 300, 640, () =>
        {
            var list = Library(panel, true);
            var grid = Descendants(list).OfType<System.Windows.Controls.Primitives.UniformGrid>().Single();
            Assert.AreEqual(4, grid.Columns); Assert.AreEqual(240d, LibraryViewport(panel).ActualHeight, .01);
            var rows = list.Items.Cast<ListBoxItem>().ToArray(); Assert.HasCount(count, rows);
            var bounds = rows.Select(row => row.TransformToAncestor(list).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight))).ToArray();
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                Assert.IsTrue(row.ActualHeight > 0 && row.ActualHeight <= 70,
                    $"A single library row must keep its natural card height instead of filling the 240 DIP viewport: {row.ActualHeight}.");
                Assert.AreEqual(bounds[0].Top, bounds[index].Top, .01, "Three or four cards must occupy one row of the four-column library.");
                if (index > 0) Assert.IsTrue(bounds[index].Left >= bounds[index - 1].Right - .01);
                var image = Descendants(row).OfType<System.Windows.Controls.Image>().Single();
                Assert.IsNotNull(image.Source);
                Assert.IsTrue(image.ActualWidth <= AccessoryEditorPanel.ThumbnailEdge + .01 && image.ActualHeight <= AccessoryEditorPanel.ThumbnailEdge + .01);
            }
            Assert.AreSame(rows[0], list.SelectedItem);
            var scroll = LibraryViewport(panel);
            Assert.AreEqual(0d, scroll.ScrollableHeight, .01, "A single natural-height row should leave free space in the library viewport.");
            Assert.IsTrue(grid.ActualHeight <= 70, "The items panel must not stretch its only row to the viewport height.");
        });
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnavailableImageCardsKeepTheirConfigurationAndRecoverAfterRebinding(bool corrupt)
    {
        var root = NewRoot(); var initial = new AccessorySettingsSession(root);
        initial.Stage(AccessoryConfiguration.Empty);
        var imported = initial.ImportPng(WriteFictionalTransparentPng(root, 211)) with
        {
            Name = "Fictional unavailable image",
            Layer = AccessoryLayer.Behind,
            Order = 7,
            SizePercent = 173,
            BaseAngleDegrees = 29,
            ImagePivotX = .31,
            ImagePivotY = .74,
            BodyAnchorX = .63,
            BodyAnchorY = .22,
            ConnectionStrength = 41,
            LockRotation = false,
            RotationDamping = 23,
            ReturnStrength = 37,
            LimitSwing = true,
            MaxSwingDegrees = 83,
            GravityInfluence = 57
        };
        var duplicate = imported with { Id = "fictional-unavailable-copy", Name = "Fictional unavailable copy", IsEquipped = false, Order = 8 };
        var builtin = new AccessoryItem("fictional-healthy-builtin", "Fictional healthy builtin", "builtin-sprout", 64, 64);
        initial.Stage(new([imported, duplicate, builtin])); Assert.IsTrue(initial.TrySave());
        var saved = initial.SavedConfiguration;
        var manifestBytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var assetPath = AccessorySettingsStore.GetAssetPath(root, imported.AssetId);
        var originalImage = File.ReadAllBytes(assetPath); var backupPath = assetPath + ".fictional-backup";
        if (corrupt) File.WriteAllText(assetPath, "fictional invalid PNG");
        else File.Move(assetPath, backupPath);
        try
        {
            var session = new AccessorySettingsSession(root);
            var panel = new AccessoryEditorPanel { Width = 240 }; panel.Bind(session);
            var list = Library(panel, true);
            var rows = saved.Items.Select(item => LibraryCards(panel).Single(row => (string)row.Tag == item.Id)).ToArray(); Assert.HasCount(3, rows);
            foreach (var row in rows)
            {
                var warning = ((Grid)row.Content).Children.OfType<Border>().Single();
                Assert.AreEqual(Visibility.Collapsed, warning.Visibility, "An image that has not been loaded is not a failed image.");
                Assert.IsFalse(AutomationProperties.GetItemStatus(row).Contains("图片不可用", StringComparison.Ordinal));
            }
            Assert.AreEqual(0, panel.ThumbnailDecodeCount);
            WithWindow(panel, 300, 640, () =>
            {
                Assert.AreEqual(saved, session.DraftConfiguration); Assert.AreEqual(saved, session.SavedConfiguration);
                Assert.AreEqual(imported, panel.SelectedItem); Assert.AreSame(rows[0], list.SelectedItem);
                Assert.AreEqual(2, panel.ThumbnailDecodeCount, "Cards using one unavailable immutable asset must share its failure cache.");
                foreach (var row in rows.Take(2))
                {
                    var item = saved.Items.Single(item => item.Id == (string)row.Tag);
                    var warning = Descendants(row).OfType<Border>().Single(border => AutomationProperties.GetName(border) == "图片不可用");
                    Assert.IsTrue(warning.IsVisible && warning.ActualWidth > 0 && warning.ActualHeight > 0, "A failed image must have a visible card warning.");
                    Assert.AreEqual("!", ((TextBlock)warning.Child).Text);
                    Assert.AreEqual(item.Name + " · 图片不可用", row.ToolTip);
                    Assert.AreEqual((item.IsEquipped ? "已佩戴" : "未佩戴") + " · 图片不可用", AutomationProperties.GetItemStatus(row));
                    StringAssert.Contains(AutomationProperties.GetName(row), item.Name);
                    Assert.IsNull(Descendants(row).OfType<System.Windows.Controls.Image>().Single().Source);
                }
                var healthy = rows[2];
                Assert.AreEqual(builtin.Name, healthy.ToolTip); Assert.AreEqual("已佩戴", AutomationProperties.GetItemStatus(healthy));
                Assert.AreEqual(Visibility.Collapsed, Descendants(healthy).OfType<Border>().Single(border => AutomationProperties.GetName(border) == "图片不可用").Visibility);
                Assert.IsNotNull(Descendants(healthy).OfType<System.Windows.Controls.Image>().Single().Source, "The builtin image fallback must not be treated as a failed source.");
                var decoded = panel.ThumbnailDecodeCount; panel.Refresh(); Pump();
                Assert.AreEqual(decoded, panel.ThumbnailDecodeCount, "Refreshing a failed card must not repeatedly read its asset.");
                CollectionAssert.AreEqual(manifestBytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
                if (corrupt) File.WriteAllBytes(assetPath, originalImage);
                else File.Move(backupPath, assetPath);
                var repaired = new AccessorySettingsSession(root); panel.Bind(repaired); Pump();
                Assert.AreEqual(saved, repaired.DraftConfiguration); Assert.AreEqual(saved, repaired.SavedConfiguration);
                Assert.AreEqual(imported, panel.SelectedItem); Assert.AreSame(rows[0], list.SelectedItem);
                foreach (var row in rows)
                {
                    var item = saved.Items.Single(item => item.Id == (string)row.Tag);
                    var image = Descendants(row).OfType<System.Windows.Controls.Image>().Single();
                    Assert.IsNotNull(image.Source, "Rebinding a repaired asset must clear its old failed thumbnail cache.");
                    Assert.IsTrue(image.ActualWidth <= AccessoryEditorPanel.ThumbnailEdge + .01 && image.ActualHeight <= AccessoryEditorPanel.ThumbnailEdge + .01,
                        "A small one-row library must keep images at icon size.");
                    Assert.AreEqual(Visibility.Collapsed, Descendants(row).OfType<Border>().Single(border => AutomationProperties.GetName(border) == "图片不可用").Visibility);
                    Assert.AreEqual(item.Name, row.ToolTip);
                    Assert.AreEqual(item.IsEquipped ? "已佩戴" : "未佩戴", AutomationProperties.GetItemStatus(row));
                }
                Assert.AreSame(Descendants(rows[0]).OfType<System.Windows.Controls.Image>().Single().Source,
                    Descendants(rows[1]).OfType<System.Windows.Controls.Image>().Single().Source,
                    "Normal cards must still share a cached thumbnail for the same immutable asset.");
                Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
                CollectionAssert.AreEqual(manifestBytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            });
        }
        finally
        {
            if (File.Exists(backupPath)) File.Move(backupPath, assetPath);
            else File.WriteAllBytes(assetPath, originalImage);
        }
    }

    [STATestMethod]
    [DataRow("picker-cancel")]
    [DataRow("image-cancel")]
    [DataRow("image-confirm")]
    public void CreateAccessoryStartsWithTransparentPngAndKeepsConfirmationInTheDraft(string outcome)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration; var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var source = WriteFictionalTransparentPng(root, 100);
        var imageOpened = false;
        panel.ImagePicker = () => outcome == "picker-cancel" ? null : source;
        panel.UnifiedEditor = (draft, _, _, _) => { imageOpened = true; return outcome == "image-cancel" ? null : Complete(draft, draft.Original with { SizePercent = 185 }); };
        WithWindow(panel, 320, 700, () =>
        {
            var create = Descendants(panel.ActionTools).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "创建新配饰");
            ((IInvokeProvider)new ButtonAutomationPeer(create).GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
            Assert.AreEqual(outcome != "picker-cancel", imageOpened);
            if (outcome == "picker-cancel")
            {
                Assert.AreEqual(saved, session.DraftConfiguration); Assert.AreEqual(AccessoryEditorSection.Library, panel.SelectedSection);
            }
            else
            {
                Assert.AreEqual(saved.Items.Count + 1, session.DraftConfiguration.Items.Count);
                Assert.AreEqual(AccessoryEditorSection.Placement, panel.SelectedSection);
                Assert.AreEqual(outcome == "image-confirm" ? 185d : 100d, panel.SelectedItem!.SizePercent);
                Assert.IsTrue(session.IsDirty);
            }
            Assert.AreEqual(saved, session.SavedConfiguration);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            panel.CancelDraft(); Assert.AreEqual(saved, session.DraftConfiguration);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        });
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void HostCancellationDiscardsBufferedInputsBeforeFocusLeaves(bool valid)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration; var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement);
        var sink = new Button { Content = "Fictional focus target" };
        var host = new StackPanel(); host.Children.Add(panel); host.Children.Add(sink);
        WithWindow(host, 320, 850, () =>
        {
            panel.Edit(item => item with { SizePercent = 140 });
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
            Assert.IsTrue(input.Focus()); Pump(); input.Text = valid ? "Fictional discarded name" : " ";
            session.CancelPreview(); panel.DiscardInputsAndRefresh();
            Assert.IsTrue(sink.Focus()); Pump();
            Assert.AreEqual(saved, session.DraftConfiguration); Assert.IsFalse(session.IsDirty);
            Assert.AreEqual(saved.Items[0].Name, input.Text);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        });
    }

[STATestMethod]
    public void BindingAndRefreshingUnderADisabledParentPreservesNameAndEditorEditability()
    {
        var session = new AccessorySettingsSession(NewRoot()); var panel = new AccessoryEditorPanel();
        var host = new StackPanel { IsEnabled = false }; host.Children.Add(panel);
        panel.Bind(session); panel.Refresh(); panel.SelectSection(AccessoryEditorSection.Placement);
        WithWindow(host, 320, 900, () =>
        {
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
            var edit = Descendants(panel).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "编辑配饰");
            Assert.IsFalse(input.IsEnabled); Assert.IsFalse(edit.IsEnabled);
            for (var pass = 0; pass < 2; pass++)
            {
                host.IsEnabled = true; Pump(); Assert.IsTrue(input.IsEnabled); Assert.IsTrue(edit.IsEnabled);
                Assert.IsTrue(input.Focus()); Pump(); Assert.AreSame(input, Keyboard.FocusedElement);
                if (pass == 0) { host.IsEnabled = false; panel.Refresh(); Pump(); }
            }
        });
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * image.PixelHeight]; image.CopyPixels(pixels, stride, 0); return pixels;
    }

    [STATestMethod]
    [DataRow("double")]
    [DataRow("menu")]
    public void GroupedLibraryWearActionsEnforceEightAndOnlySaveOnApply(string action)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        session.Stage(new(Enumerable.Range(0, 9).Select(index =>
            new AccessoryItem("fictional-wear-" + index, "Fictional wear " + index, "builtin-sprout", 64, 64)
            { IsEquipped = index < 8, Order = index })));
        Assert.IsTrue(session.TrySave()); var saved = session.SavedConfiguration;
        var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel { Width = 240 }; panel.Bind(session);
        WithWindow(panel, 300, 640, () =>
        {
            Assert.HasCount(8, Library(panel, true).Items); Assert.HasCount(1, Library(panel, false).Items);
            Assert.IsFalse(Descendants(panel).OfType<TextBlock>().Any(text => text.Text == "配饰库"));
            Assert.IsFalse(Descendants(panel).OfType<CheckBox>().Any(control => Equals(control.Content, "佩戴")));
            void WearAction(string id)
            {
                var row = LibraryCards(panel).Single(card => (string)card.Tag == id);
                if (action == "double")
                {
                    var input = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.PreviewMouseDoubleClickEvent };
                    row.RaiseEvent(input); Assert.IsTrue(input.Handled);
                }
                else
                {
                    var menu = row.ContextMenu!; menu.IsOpen = true; Pump();
                    var command = (System.Windows.Controls.MenuItem)menu.Items[0];
                    Assert.AreEqual(session.DraftConfiguration.Items.Single(item => item.Id == id).IsEquipped ? "取消佩戴" : "佩戴", command.Header);
                    command.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); menu.IsOpen = false;
                }
                Pump();
            }
            WearAction("fictional-wear-8");
            Assert.AreEqual(saved, session.DraftConfiguration); StringAssert.Contains(panel.StatusMessage, "8 件");
            WearAction("fictional-wear-0");
            Assert.HasCount(7, Library(panel, true).Items); Assert.HasCount(2, Library(panel, false).Items);
            WearAction("fictional-wear-8");
            Assert.HasCount(8, Library(panel, true).Items); Assert.HasCount(1, Library(panel, false).Items);
            Assert.IsFalse(session.DraftConfiguration.Items.Single(item => item.Id == "fictional-wear-0").IsEquipped);
            Assert.IsTrue(session.DraftConfiguration.Items.Single(item => item.Id == "fictional-wear-8").IsEquipped);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            panel.ApplyDraft(); Assert.AreEqual(session.DraftConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
        });
    }

    [STATestMethod]
    [DataRow("equipped")]
    [DataRow("unequipped")]
    [DataRow("cross-layer")]
    [DataRow("cross-group")]
    public void ActualLibraryDropEventsRespectTheirGroupAndLayerAndSuppressTheTrailingClick(string action)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var a = new AccessoryItem("drag-a", "Fictional drag A", "builtin-sprout", 64, 64) { Layer = AccessoryLayer.Behind, Order = 0, SizePercent = 141 };
        var b = a with { Id = "drag-b", Name = "Fictional drag B", Order = 9, ConnectionStrength = 42 };
        var front = a with { Id = "drag-front", Name = "Fictional drag front", Layer = AccessoryLayer.Front, Order = 3 };
        var uA = a with { Id = "drag-ua", Name = "Fictional drag unworn A", IsEquipped = false, Order = 7 };
        var uB = front with { Id = "drag-ub", Name = "Fictional drag unworn B", IsEquipped = false, Order = 8 };
        session.Stage(new([a, uB, front, uA, b])); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration; var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel { Width = 240 }; panel.Bind(session); panel.SelectItem(front.Id);
        WithWindow(panel, 300, 640, () =>
        {
            CollectionAssert.AreEqual(new[] { a.Id, b.Id, front.Id }, Library(panel, true).Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).ToArray());
            var from = action == "unequipped" ? uB : a;
            var to = action switch { "unequipped" => uA, "cross-layer" => front, "cross-group" => uA, _ => b };
            var target = LibraryCards(panel).Single(row => (string)row.Tag == to.Id);
            var allowed = action is "equipped" or "unequipped";
            var over = CreateLibraryDragEvent(panel, from.Id, target, DragDrop.PreviewDragOverEvent);
            target.RaiseEvent(over); Assert.IsTrue(over.Handled);
            Assert.AreEqual(allowed ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None, over.Effects);
            var drop = CreateLibraryDragEvent(panel, from.Id, target, DragDrop.PreviewDropEvent);
            target.RaiseEvent(drop); Pump(); Assert.IsTrue(drop.Handled);
            Assert.AreEqual(allowed ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None, drop.Effects);
            Assert.AreEqual(front.Id, panel.SelectedItem!.Id, "Reordering must retain the previously selected accessory.");
            var reordered = session.DraftConfiguration;
            var source = LibraryCards(panel).Single(row => (string)row.Tag == from.Id);
            var trailingClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.PreviewMouseDoubleClickEvent };
            source.RaiseEvent(trailingClick); Assert.IsTrue(trailingClick.Handled);
            var mouseUp = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent };
            source.RaiseEvent(mouseUp); Assert.IsTrue(mouseUp.Handled);
            Assert.AreEqual(reordered, session.DraftConfiguration, "A drag's trailing click must not toggle wearing or select another item.");
            Assert.AreEqual(front.Id, panel.SelectedItem!.Id);
            if (action == "equipped")
            {
                CollectionAssert.AreEqual(new[] { b.Id, a.Id, front.Id }, Library(panel, true).Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).ToArray());
                Assert.AreEqual(b with { Order = 0 }, reordered.Items.Single(item => item.Id == b.Id));
                Assert.AreEqual(a with { Order = 1 }, reordered.Items.Single(item => item.Id == a.Id));
                CollectionAssert.AreEqual(saved.Items.Select(item => item.Id).ToArray(), reordered.Items.Select(item => item.Id).ToArray());
            }
            else if (action == "unequipped")
            {
                CollectionAssert.AreEqual(new[] { uA.Id, uB.Id }, Library(panel, false).Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).ToArray());
                CollectionAssert.AreEqual(new[] { a.Id, uA.Id, front.Id, uB.Id, b.Id }, reordered.Items.Select(item => item.Id).ToArray());
                Assert.AreEqual(uA, reordered.Items.Single(item => item.Id == uA.Id)); Assert.AreEqual(uB, reordered.Items.Single(item => item.Id == uB.Id));
            }
            else Assert.AreEqual(saved, reordered, "Cross-group or cross-layer drops must not silently change wearing or layers.");
            Assert.AreEqual(front, reordered.Items.Single(item => item.Id == front.Id));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            panel.ApplyDraft(); Assert.AreEqual(reordered, new AccessorySettingsSession(root).SavedConfiguration);
        });
    }

    [STATestMethod]
    [DataRow(true, 1)]
    [DataRow(true, -1)]
    [DataRow(false, 1)]
    [DataRow(false, -1)]
    public void ActualOrderButtonsUseTheSameMixedLibraryCandidatesAsDragging(bool equipped, int direction)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var a = new AccessoryItem("arrow-a", "Fictional arrow A", "builtin-sprout", 64, 64) { Layer = AccessoryLayer.Behind, Order = 0 };
        var u = a with { Id = "arrow-u", Name = "Fictional arrow unworn U", IsEquipped = false, Order = 1, SizePercent = 147 };
        var b = a with { Id = "arrow-b", Name = "Fictional arrow B", Order = 2, ConnectionStrength = 59 };
        var v = u with { Id = "arrow-v", Name = "Fictional arrow unworn V", Layer = AccessoryLayer.Front, Order = 8 };
        session.Stage(new([a, u, b, v])); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration; var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var selected = equipped ? direction == 1 ? a : b : direction == 1 ? u : v;
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectItem(selected.Id); panel.SelectSection(AccessoryEditorSection.Placement);
        WithWindow(panel, 320, 1000, () =>
        {
            var button = Descendants(panel).OfType<Button>().Single(control => AutomationProperties.GetName(control) == (direction == 1 ? "上移" : "下移"));
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
            Assert.AreEqual(selected.Id, panel.SelectedItem!.Id);
            var reordered = session.DraftConfiguration;
            if (equipped)
            {
                CollectionAssert.AreEqual(new[] { b.Id, a.Id }, Library(panel, true).Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).ToArray());
                Assert.AreEqual(a with { Order = 1 }, reordered.Items.Single(item => item.Id == a.Id));
                Assert.AreEqual(b with { Order = 0 }, reordered.Items.Single(item => item.Id == b.Id));
                Assert.AreEqual(u, reordered.Items.Single(item => item.Id == u.Id)); Assert.AreEqual(v, reordered.Items.Single(item => item.Id == v.Id));
                CollectionAssert.AreEqual(saved.Items.Select(item => item.Id).ToArray(), reordered.Items.Select(item => item.Id).ToArray());
            }
            else
            {
                CollectionAssert.AreEqual(new[] { v.Id, u.Id }, Library(panel, false).Items.Cast<ListBoxItem>().Select(row => (string)row.Tag).ToArray());
                CollectionAssert.AreEqual(new[] { a.Id, v.Id, b.Id, u.Id }, reordered.Items.Select(item => item.Id).ToArray());
                foreach (var item in saved.Items) Assert.AreEqual(item, reordered.Items.Single(candidate => candidate.Id == item.Id));
            }
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            panel.ApplyDraft(); Assert.AreEqual(reordered, new AccessorySettingsSession(root).SavedConfiguration);
        });
    }

    [STATestMethod]
    [DataRow("first-cancel")]
    [DataRow("second-cancel")]
    [DataRow("accept")]
    [DataRow("write-failure")]
    [DataRow("protected-corrupt")]
    [DataRow("protected-future")]
    public void DeleteToolRequiresExactlyTwoConfirmationsAndPreservesOtherDraftsAndCancelledInput(string outcome)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var a = new AccessoryItem("delete-a", "Fictional delete A", "builtin-sprout", 64, 64);
        var b = a with { Id = "delete-b", Name = "Fictional delete B" };
        session.Stage(new([a, b])); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration; var bytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var expectedDiskBytes = bytes;
        session.Stage(new([a, b with { SizePercent = 142 }])); var draft = session.DraftConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement);
        var host = new StackPanel(); host.Children.Add(panel); host.Children.Add(panel.ActionTools);
        var sink = new Button { Content = "Fictional deletion focus sink" }; host.Children.Add(sink);
        FileStream? locked = null;
        try
        {
            WithWindow(host, 320, 1000, () =>
            {
                var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰名称");
                Assert.IsTrue(input.Focus()); Pump(); input.Text = "Fictional unsaved deletion name";
                var calls = new List<int>();
                panel.DeleteConfirmation = (_, step) =>
                {
                    calls.Add(step); Assert.IsTrue(sink.Focus()); Pump();
                    Assert.AreEqual(draft, session.DraftConfiguration, "Opening either confirmation must not commit the current text buffer.");
                    if (outcome == "write-failure" && step == 2) locked = new(AccessorySettingsStore.GetManifestPath(root), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    if (outcome.StartsWith("protected-", StringComparison.Ordinal) && step == 2)
                    {
                        File.WriteAllText(AccessorySettingsStore.GetManifestPath(root), outcome == "protected-future" ? "{\"accessorySchemaVersion\":99,\"items\":[]}" : "{fictional corrupt manifest}");
                        expectedDiskBytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
                    }
                    return outcome != "first-cancel" && (outcome != "second-cancel" || step != 2);
                };
                var delete = Descendants(panel.ActionTools).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "删除配饰");
                delete.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                Assert.IsTrue(delete.Focus()); Pump();
                Assert.AreEqual(draft, session.DraftConfiguration, "Mouse-down before the delete button gains real focus must suppress the input's blur commit.");
                Assert.AreEqual("Fictional unsaved deletion name", input.Text);
                ((IInvokeProvider)new ButtonAutomationPeer(delete).GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
                locked?.Dispose(); locked = null;
                CollectionAssert.AreEqual(outcome == "first-cancel" ? new[] { 1 } : new[] { 1, 2 }, calls.ToArray());
                if (outcome == "accept")
                {
                    Assert.AreEqual(new AccessoryConfiguration([b]), new AccessorySettingsSession(root).SavedConfiguration);
                    Assert.AreEqual(new AccessoryConfiguration([b with { SizePercent = 142 }]), session.DraftConfiguration);
                    Assert.AreEqual(b.Id, panel.SelectedItem!.Id);
                }
                else
                {
                    Assert.AreEqual(saved, session.SavedConfiguration); Assert.AreEqual(draft, session.DraftConfiguration);
                    Assert.AreEqual("Fictional unsaved deletion name", input.Text);
                    Assert.AreEqual(a.Id, panel.SelectedItem!.Id);
                    CollectionAssert.AreEqual(expectedDiskBytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
                    if (outcome.StartsWith("protected-", StringComparison.Ordinal))
                    {
                        Assert.IsTrue(session.IsWriteProtected);
                        foreach (var tool in Descendants(panel.ActionTools).OfType<Button>()) Assert.IsFalse(tool.IsEnabled, "A newly protected manifest must disable every library action.");
                        foreach (var control in Descendants(panel).OfType<Control>().Where(control => control is TextBox or Slider or ComboBox or CheckBox))
                            Assert.IsFalse(control.IsEnabled, "A newly protected manifest must disable every editor input while retaining its buffer.");
                        Assert.IsFalse(Descendants(panel.SaveActions).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "应用").IsEnabled);
                    }
                }
            });
        }
        finally { locked?.Dispose(); }
    }

    private static AccessoryEditResult Complete(AccessoryEditDraft draft, AccessoryItem item)
    {
        var source = AccessoryEditorSourceAsset.FromBitmap(BuiltinAccessoryImages.Get("builtin-sprout")!);
        var image = AccessoryImageAsset.FromRenderedPng(source.CopyPngBytes());
        var project = AccessoryEditorProject.FromImage(image, draft.Revision);
        return new(project, image, item with { AssetId = image.AssetId, PixelWidth = image.PixelWidth, PixelHeight = image.PixelHeight,
            ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = draft.Revision, IsPendingCompletion = false });
    }

    private static string WriteFictionalTransparentPng(string root, int color)
    {
        Directory.CreateDirectory(root);
        var pixels = new byte[4 * 4 * 4];
        pixels[20] = (byte)color; pixels[21] = 110; pixels[22] = 190; pixels[23] = 255;
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(root, $"fictional-thumbnail-{color}.png");
        using var stream = File.Create(path); encoder.Save(stream);
        return path;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in LogicalDescendants<T>(child)) yield return nested;
    }

    private static System.Windows.DragEventArgs CreateLibraryDragEvent(AccessoryEditorPanel panel, string id, ListBoxItem target, RoutedEvent routedEvent)
    {
        // WPF constructs its real drag event internally; use that constructor and route through the production handlers.
        var constructor = typeof(System.Windows.DragEventArgs).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, types: [typeof(System.Windows.IDataObject), typeof(System.Windows.DragDropKeyStates), typeof(System.Windows.DragDropEffects), typeof(DependencyObject), typeof(Point)], modifiers: null);
        Assert.IsNotNull(constructor, "The target WPF runtime must expose its internal five-argument drag-event constructor.");
        var input = (System.Windows.DragEventArgs)constructor.Invoke([panel.CreateDragData(id), System.Windows.DragDropKeyStates.LeftMouseButton,
            System.Windows.DragDropEffects.Move, target, new Point(4, 4)]);
        input.RoutedEvent = routedEvent;
        return input;
    }

    private static ListBox Library(AccessoryEditorPanel panel, bool worn) => LogicalDescendants<ListBox>(panel)
        .Single(group => AutomationProperties.GetName(group) == (worn ? "已佩戴配饰" : "未佩戴配饰"));

    private static ListBoxItem[] LibraryCards(AccessoryEditorPanel panel) =>
        new[] { Library(panel, true), Library(panel, false) }.SelectMany(group => group.Items.Cast<ListBoxItem>()).ToArray();

    private static ScrollViewer LibraryViewport(AccessoryEditorPanel panel) => LogicalDescendants<ScrollViewer>(panel)
        .Single(viewport => AutomationProperties.GetName(viewport).StartsWith("配饰库", StringComparison.Ordinal));

    private static void Pump()
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void Capture(FrameworkElement control, string name)
    {
        control.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(control);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(control.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(control.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        // Render only this owned control, on the same opaque warm paper used by Home.
        var localBounds = new Rect(0, 0, control.ActualWidth, control.ActualHeight);
        var offset = VisualTreeHelper.GetOffset(control);
        var snapshot = new DrawingVisual();
        using (var drawing = snapshot.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(253, 252, 249)), null, localBounds);
            drawing.DrawRectangle(new VisualBrush(control)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(offset.X, offset.Y, control.ActualWidth, control.ActualHeight),
                Stretch = Stretch.Fill,
                AutoLayoutContent = false,
            }, null, localBounds);
        }
        bitmap.Render(snapshot);
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-ui-editor-harness", "screenshots"); Directory.CreateDirectory(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(root, $"{name}-{Guid.NewGuid():N}.png")); encoder.Save(stream);
    }

    private static void AssertFaceIcons(IEnumerable<Button> actions)
    {
        foreach (var button in actions)
        {
            Assert.AreEqual(32d, button.ActualWidth, .01); Assert.AreEqual(32d, button.ActualHeight, .01);
            Assert.IsInstanceOfType<Viewbox>(button.Content);
            Assert.HasCount(1, Descendants(button).OfType<System.Windows.Shapes.Path>());
        }
    }

    private static void WithWindow(FrameworkElement content, double width, double height, Action verify)
    {
        var window = new Window
        {
            Content = content,
            Width = width,
            Height = height,
            Left = -10000,
            Top = -10000,
            Background = new SolidColorBrush(Color.FromRgb(253, 252, 249)),
            ShowInTaskbar = false,
            Title = "Fictional accessory editor UI verification",
        };
        try { window.Show(); Pump(); verify(); }
        finally { window.Close(); }
    }
}
