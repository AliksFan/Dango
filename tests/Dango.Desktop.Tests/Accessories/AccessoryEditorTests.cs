using System.Globalization;
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
    public void ImageConfirmationOnlyStagesAndExplicitApplySurvivesReload()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var saved = session.SavedConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var previewCount = 0; var applyCount = 0;
        panel.PreviewChanged += _ => previewCount++; panel.Applied += _ => applyCount++;
        panel.ImageEditor = (item, _) => item with { SizePercent = 185, BaseAngleDegrees = -45, ImagePivotX = .18, ImagePivotY = .72 };
        panel.EditSelectedImage();

        Assert.AreEqual(185, panel.SelectedItem!.SizePercent);
        Assert.AreEqual(-45, panel.SelectedItem.BaseAngleDegrees);
        Assert.AreEqual(.18, panel.SelectedItem.ImagePivotX);
        Assert.AreEqual(saved, session.SavedConfiguration);
        Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
        Assert.AreEqual(1, previewCount); Assert.AreEqual(0, applyCount);

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
        panel.ImageEditor = (_, _) => null;
        panel.EditSelectedImage();
        Assert.AreEqual(priorDraft, session.DraftConfiguration);

        panel.ImageEditor = (item, _) =>
        {
            panel.CancelDraft();
            return item with { SizePercent = 340, Softness = 100 };
        };
        panel.EditSelectedImage();
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        Assert.IsFalse(session.IsDirty);
    }

    [STATestMethod]
    public void RebindingWhileImageEditorIsOpenCannotWriteResultIntoAnotherSession()
    {
        var original = new AccessorySettingsSession(NewRoot());
        var replacement = new AccessorySettingsSession(NewRoot(), "bowler");
        var panel = new AccessoryEditorPanel(); panel.Bind(original);
        panel.ImageEditor = (item, _) => { panel.Bind(replacement); return item with { SizePercent = 350 }; };
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
    public void ViewZoomChangesNoAccessoryOrOriginalPixelAndCancelHasNoResult()
    {
        var image = BuiltinAccessoryImages.Get("builtin-sprout")!;
        var originalPixels = Pixels(image);
        var item = new AccessoryItem("image", "Image", "builtin-sprout", 64, 64) { BaseAngleDegrees = 35, ImagePivotX = .1, ImagePivotY = .8 };
        var editor = new AccessoryImageEditorWindow(item, image);
        editor.SetViewZoom(4); editor.SetViewZoom(.25);
        Assert.AreEqual(item, editor.Draft);
        Assert.IsNull(editor.Result);
        CollectionAssert.AreEqual(originalPixels, Pixels(image));
        editor.SetImagePivot(new(.9, .2));
        Assert.AreEqual(item with { ImagePivotX = .9, ImagePivotY = .2 }, editor.Draft);
        Assert.IsNull(editor.Result);
        CollectionAssert.AreEqual(originalPixels, Pixels(image));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.SetViewZoom(0));
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
            CollectionAssert.AreEqual(new[] { "复位当前配饰", "取消", "应用" }, actions.Select(AutomationProperties.GetName).ToArray());
            AssertFaceIcons(actions);
            var apply = actions[2]; Assert.IsFalse(apply.IsEnabled); Assert.AreEqual("Primary", apply.Tag);
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
        panel.Children.Remove(panel.SaveActions); host.Children.Add(panel.SaveActions);
        WithWindow(host, 260, 640, () =>
        {
            var apply = Descendants(panel.SaveActions).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "应用");
            Assert.AreSame(home.FindResource("FaceIconButton"), apply.Style);
            var import = Descendants(panel).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "导入 PNG");
            Assert.AreSame(home.FindResource(typeof(Button)), import.Style);
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
    public void ParameterGroupsPreserveEverySliderAndCompactDirectNumberEntry()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var panel = new AccessoryEditorPanel { Width = 125 }; panel.Bind(session);
        WithWindow(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 185, 640, () =>
        {
            var groups = Descendants(panel).OfType<Expander>().ToArray(); Assert.HasCount(3, groups);
            Assert.HasCount(1, groups.Where(group => group.IsExpanded));
            var collapsedHeight = panel.ActualHeight;
            Capture(panel, "compact-face-aligned-groups");
            foreach (var group in groups) group.IsExpanded = true;
            Pump(); Assert.IsTrue(panel.ActualHeight > collapsedHeight + 120, "Collapsing functional groups must actually shorten the panel.");
            var sliders = Descendants(panel).OfType<Slider>().ToArray(); Assert.HasCount(10, sliders);
            foreach (var slider in sliders)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(AutomationProperties.GetName(slider)));
                var row = (StackPanel)slider.Parent; Assert.AreEqual(2, row.Children.Count);
                var heading = (Grid)row.Children[0]; var input = heading.Children.OfType<TextBox>().Single();
                Assert.AreEqual(44d, input.Width); Assert.AreEqual(new Thickness(0, 0, 0, 1), input.BorderThickness);
                Assert.AreEqual(AutomationProperties.GetName(slider) + " 数值", AutomationProperties.GetName(input));
                var label = heading.Children.OfType<TextBlock>().Single();
                var labelBounds = label.TransformToAncestor(heading).TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight));
                var inputBounds = input.TransformToAncestor(heading).TransformBounds(new Rect(0, 0, input.ActualWidth, input.ActualHeight));
                Assert.IsTrue(labelBounds.Right <= inputBounds.Left + .01, "Compact current value must not overlap its label.");
            }
            var bodyX = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "身体附着 X 数值");
            bodyX.Text = "0.73";
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(bodyX)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            bodyX.RaiseEvent(key);
            Assert.IsTrue(key.Handled); Assert.AreEqual(.73, panel.SelectedItem!.BodyAnchorX);
            Assert.IsTrue(session.IsDirty); Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(root)));
        });
    }

    [STATestMethod]
    public void ImageWindowUsesFaceThemeAndKeepsActionsFixedAtMinimumSize()
    {
        var panel = new AccessoryEditorPanel();
        var item = new AccessoryItem("image", "Fictional image", "builtin-sprout", 64, 64);
        var editor = new AccessoryImageEditorWindow(item, BuiltinAccessoryImages.Get(item.AssetId)!);
        AccessoryEditorTheme.CopyFrom(panel, editor);
        editor.Width = 560; editor.Height = 440; editor.Left = -10000; editor.Top = -10000; editor.ShowInTaskbar = false;
        try
        {
            editor.Show(); Pump();
            Assert.AreEqual("Segoe UI, Microsoft YaHei", editor.FontFamily.Source);
            foreach (var key in new[] { "HomeInk", "HomeMuted", "HomeLine", "HomeSoft", "HomeAccent", "HomeSelected", "HomePrimaryInk", "FaceIconButton" })
                Assert.AreSame(panel.FindResource(key), editor.FindResource(key));
            var actions = Descendants(editor).OfType<Button>().Where(button => AutomationProperties.GetName(button) is "复位图片变换" or "取消" or "确认").ToArray();
            AssertFaceIcons(actions); Assert.HasCount(3, actions);
            var parameterScroll = Descendants(editor).OfType<ScrollViewer>().Single(scroll => scroll.Content is StackPanel);
            var accept = actions.Single(button => AutomationProperties.GetName(button) == "确认");
            var before = accept.TransformToAncestor((Visual)editor.Content).TransformBounds(new Rect(0, 0, accept.ActualWidth, accept.ActualHeight));
            parameterScroll.ScrollToBottom(); Pump();
            var after = accept.TransformToAncestor((Visual)editor.Content).TransformBounds(new Rect(0, 0, accept.ActualWidth, accept.ActualHeight));
            Assert.AreEqual(before, after, "Image confirmation must remain fixed outside parameter scrolling.");
            Assert.HasCount(5, Descendants(editor).OfType<Slider>());
            editor.SetViewZoom(1.5); Pump(); Assert.AreEqual(item, editor.Draft); Assert.IsNull(editor.Result);
            Capture((FrameworkElement)editor.Content, "face-aligned-image-editor-minimum");
        }
        finally { editor.Close(); }
    }

    [STATestMethod]
    [DataRow("name")]
    [DataRow("bodyX")]
    public void FocusedDirectInputCanApplyBeforeBlur(string field)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root); Assert.IsTrue(session.TrySave());
        var precise = session.SavedConfiguration.Items[0] with { BodyAnchorX = .731234, BodyAnchorY = .287654, BaseAngleDegrees = 12.123456 };
        session.Stage(new(session.SavedConfiguration.Items.Select(item => item.Id == precise.Id ? precise : item)));
        Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        WithWindow(panel, 280, 900, () =>
        {
            var name = field == "name" ? "配饰名称" : "身体附着 X 数值";
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == name);
            var apply = Descendants(panel.SaveActions).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "应用");
            Assert.IsFalse(apply.IsEnabled);
            Assert.IsTrue(input.Focus(), "The regression must have a real focused WPF text control."); Pump();
            input.Text = field == "name" ? "Fictional focused rename" : .73.ToString(CultureInfo.CurrentCulture);
            Pump(); Assert.AreSame(input, Keyboard.FocusedElement);
            Assert.IsTrue(apply.IsEnabled, "Typing valid unsaved input must make Apply usable before focus leaves the text control.");
            ((IInvokeProvider)new ButtonAutomationPeer(apply).GetPattern(PatternInterface.Invoke)!).Invoke();
            Pump();
            var loaded = new AccessorySettingsSession(root).SavedConfiguration.Items.Single(item => item.Id == panel.SelectedItem!.Id);
            if (field == "name") Assert.AreEqual("Fictional focused rename", loaded.Name);
            else Assert.AreEqual(.73, loaded.BodyAnchorX);
            if (field == "name") Assert.AreEqual(precise.BodyAnchorX, loaded.BodyAnchorX, "Untouched rounded text must not truncate the saved double.");
            Assert.AreEqual(precise.BodyAnchorY, loaded.BodyAnchorY);
            Assert.AreEqual(precise.BaseAngleDegrees, loaded.BaseAngleDegrees);
            Assert.IsFalse(session.IsDirty); Assert.IsFalse(apply.IsEnabled);
        });
    }

    [STATestMethod]
    public void ApplyRejectsInvalidBufferedBatchWithoutWritingOrDiscardingText()
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root); Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration;
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        WithWindow(panel, 280, 900, () =>
        {
            var inputs = Descendants(panel).OfType<TextBox>().ToDictionary(AutomationProperties.GetName);
            Assert.IsTrue(inputs["身体附着 X 数值"].Focus()); Pump();
            inputs["身体附着 X 数值"].Text = "2";
            inputs["身体附着 Y 数值"].Text = .73.ToString(CultureInfo.CurrentCulture);
            inputs["配饰名称"].Text = "Fictional buffered rename";
            panel.ApplyDraft();
            Assert.AreEqual(saved, session.DraftConfiguration, "An invalid number must not partially stage other buffered values.");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            Assert.AreEqual("2", inputs["身体附着 X 数值"].Text, "Invalid text must remain available for correction.");
            Assert.AreEqual(.73.ToString(CultureInfo.CurrentCulture), inputs["身体附着 Y 数值"].Text);
            Assert.AreEqual("Fictional buffered rename", inputs["配饰名称"].Text);
            StringAssert.Contains(panel.StatusMessage, "身体附着 X");
            panel.CancelDraft();
            Assert.AreEqual(saved, session.DraftConfiguration);
            Assert.AreEqual(saved.Items[0].Name, inputs["配饰名称"].Text);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        });
    }

    [STATestMethod]
    public void ImageWindowCopiesBackgroundAndInkAsOneTheme()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.Resources["HomeBackground"] = Brushes.Black;
        home.Resources["HomeInk"] = Brushes.White;
        home.Resources["HomePrimaryInk"] = Brushes.White;
        var item = new AccessoryItem("image", "Fictional contrast image", "builtin-sprout", 64, 64);
        var editor = new AccessoryImageEditorWindow(item, BuiltinAccessoryImages.Get(item.AssetId)!);
        AccessoryEditorTheme.CopyFrom(home, editor);
        try
        {
            Assert.AreSame(Brushes.White, editor.Foreground);
            Assert.AreSame(Brushes.Black, editor.Background, "A copied light Ink must keep its dark Home background.");
            Assert.AreSame(home.FindResource("HomeBackground"), editor.FindResource("HomeBackground"));
        }
        finally { editor.Close(); }
    }

    [STATestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public void ActualModalConfirmationValidatesFocusedInputAndPreservesUntouchedPrecision(bool valid, bool mixedBatch)
    {
        var item = new AccessoryItem("modal-input", "Fictional modal input", "builtin-sprout", 64, 64)
        {
            BaseAngleDegrees = 12.123456,
            ImagePivotX = .731234,
            ImagePivotY = .287654,
        };
        var editor = new AccessoryImageEditorWindow(item, BuiltinAccessoryImages.Get(item.AssetId)!)
        { Left = -10000, Top = -10000, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        Exception? failure = null;
        editor.Loaded += (_, _) => editor.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var input = Descendants(editor).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰尺寸 % 数值");
                Assert.IsTrue(input.Focus()); Pump();
                input.Text = valid || mixedBatch ? "185" : "500";
                var pivot = Descendants(editor).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "图片支点 Y 数值");
                if (mixedBatch) pivot.Text = "2";
                Pump();
                Assert.AreSame(input, Keyboard.FocusedElement);
                var confirm = Descendants(editor).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "确认");
                ((IInvokeProvider)new ButtonAutomationPeer(confirm).GetPattern(PatternInterface.Invoke)!).Invoke();
                Pump();
                if (!valid)
                {
                    Assert.IsTrue(editor.IsVisible, "Invalid focused input must leave the actual modal open for correction.");
                    Assert.IsNull(editor.Result); Assert.AreEqual(item, editor.Draft);
                    Assert.AreEqual(mixedBatch ? "185" : "500", input.Text);
                    if (mixedBatch) Assert.AreEqual("2", pivot.Text);
                }
            }
            catch (Exception error) { failure = error; }
            finally { editor.Close(); }
        }));
        editor.ShowDialog();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if (valid)
        {
            Assert.IsNotNull(editor.Result); Assert.AreEqual(185d, editor.Result.SizePercent);
            Assert.AreEqual(item.BaseAngleDegrees, editor.Result.BaseAngleDegrees);
            Assert.AreEqual(item.ImagePivotX, editor.Result.ImagePivotX);
            Assert.AreEqual(item.ImagePivotY, editor.Result.ImagePivotY);
        }
        else Assert.IsNull(editor.Result);
    }

    [STATestMethod]
    [DataRow("select", true)]
    [DataRow("select", false)]
    [DataRow("duplicate", true)]
    [DataRow("duplicate", false)]
    [DataRow("image", true)]
    [DataRow("image", false)]
    public void BufferedInputsStayWithTheirAccessoryAcrossDependentActions(string action, bool valid)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var a = new AccessoryItem("a", "Fictional A", "builtin-sprout", 64, 64) { BodyAnchorX = .27, BodyAnchorY = .18 };
        var b = a with { Id = "b", Name = "Fictional B", BodyAnchorX = .81, BodyAnchorY = .92 };
        session.Stage(new([a, b])); Assert.IsTrue(session.TrySave());
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        AccessoryItem? imageInput = null;
        panel.ImageEditor = (item, _) => { imageInput = item; return item with { SizePercent = 185 }; };
        WithWindow(panel, 280, 900, () =>
        {
            var inputs = Descendants(panel).OfType<TextBox>().ToDictionary(AutomationProperties.GetName);
            var input = inputs["身体附着 X 数值"];
            Assert.IsTrue(input.Focus()); Pump();
            input.Text = valid ? .73.ToString(CultureInfo.CurrentCulture) : "2";
            inputs["配饰名称"].Text = "Fictional buffered A";
            Assert.AreSame(input, Keyboard.FocusedElement);
            if (action == "select")
            {
                var list = Descendants(panel).OfType<ListBox>().Single();
                list.SelectedItem = list.Items.Cast<ListBoxItem>().Single(row => (string)row.Tag == "b");
            }
            else
            {
                var button = Descendants(panel).OfType<Button>().Single(candidate =>
                    AutomationProperties.GetName(candidate) == (action == "duplicate" ? "复制" : "图片／支点"));
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
            }
            Pump();
            Assert.AreEqual(b, session.DraftConfiguration.Items.Single(item => item.Id == b.Id), "Buffered A inputs must never overwrite B.");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)), "Dependent actions only stage; they must not save.");
            if (valid)
            {
                var committedA = session.DraftConfiguration.Items.Single(item => item.Id == a.Id);
                Assert.AreEqual(.73, committedA.BodyAnchorX); Assert.AreEqual("Fictional buffered A", committedA.Name);
                Assert.AreEqual(a.BodyAnchorY, committedA.BodyAnchorY);
                if (action == "select")
                {
                    Assert.AreEqual(b.Id, panel.SelectedItem!.Id);
                    Assert.AreEqual(b.Name, inputs["配饰名称"].Text);
                    Assert.AreEqual(b.BodyAnchorX.ToString("0.###", CultureInfo.CurrentCulture), input.Text);
                }
                else if (action == "duplicate")
                {
                    Assert.HasCount(3, session.DraftConfiguration.Items);
                    Assert.AreNotEqual(a.Id, panel.SelectedItem!.Id); Assert.AreNotEqual(b.Id, panel.SelectedItem.Id);
                    Assert.AreEqual(.73, panel.SelectedItem.BodyAnchorX);
                    Assert.AreEqual(a.BodyAnchorY, panel.SelectedItem.BodyAnchorY);
                    StringAssert.StartsWith(panel.SelectedItem.Name, "Fictional buffered A");
                }
                else
                {
                    Assert.IsNotNull(imageInput); Assert.AreEqual(.73, imageInput.BodyAnchorX);
                    Assert.AreEqual("Fictional buffered A", imageInput.Name);
                    Assert.AreEqual(185d, panel.SelectedItem!.SizePercent);
                }
            }
            else
            {
                Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration, "Invalid input must block the whole dependent action.");
                Assert.AreEqual(a.Id, panel.SelectedItem!.Id); Assert.HasCount(2, session.DraftConfiguration.Items);
                Assert.IsNull(imageInput); Assert.AreEqual("2", input.Text);
                Assert.AreEqual("Fictional buffered A", inputs["配饰名称"].Text);
                StringAssert.Contains(panel.StatusMessage, "身体附着 X");
            }
        });
    }

    [STATestMethod]
    [DataRow("layer", true)]
    [DataRow("layer", false)]
    [DataRow("equipped", true)]
    [DataRow("equipped", false)]
    [DataRow("lock", true)]
    [DataRow("lock", false)]
    [DataRow("limit", true)]
    [DataRow("limit", false)]
    public void BufferedInputDoesNotOverwriteTheFollowingChoice(string choice, bool valid)
    {
        var root = NewRoot(); var session = new AccessorySettingsSession(root);
        var item = new AccessoryItem("choice", "Fictional choice", "builtin-sprout", 64, 64) { BodyAnchorX = .27, LockRotation = false };
        session.Stage(new([item])); Assert.IsTrue(session.TrySave());
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        WithWindow(panel, 280, 1000, () =>
        {
            foreach (var group in Descendants(panel).OfType<Expander>()) group.IsExpanded = true;
            Pump();
            var input = Descendants(panel).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "身体附着 X 数值");
            Assert.IsTrue(input.Focus()); Pump(); input.Text = valid ? .73.ToString(CultureInfo.CurrentCulture) : "2";
            if (choice == "layer") Descendants(panel).OfType<ComboBox>().Single().SelectedIndex = 0;
            else
            {
                var label = choice switch { "equipped" => "佩戴", "lock" => "锁定朝向", _ => "限制摆角" };
                var control = Descendants(panel).OfType<CheckBox>().Single(candidate => (string)candidate.Content == label);
                control.IsChecked = choice == "lock";
                control.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            Pump();
            if (valid)
            {
                Assert.AreEqual(.73, panel.SelectedItem!.BodyAnchorX);
                var expected = item with { BodyAnchorX = .73 };
                expected = choice switch
                {
                    "layer" => expected with { Layer = AccessoryLayer.Behind },
                    "equipped" => expected with { IsEquipped = false },
                    "lock" => expected with { LockRotation = true },
                    _ => expected with { LimitSwing = false },
                };
                Assert.AreEqual(expected, panel.SelectedItem, "Committing buffered text must not reset the choice that triggered it.");
            }
            else
            {
                Assert.AreEqual(item, panel.SelectedItem); Assert.AreEqual("2", input.Text);
                StringAssert.Contains(panel.StatusMessage, "身体附着 X");
            }
            Assert.AreEqual(item, session.SavedConfiguration.Items.Single(), "Changing a choice only stages the draft.");
        });
    }

    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SliderCanCorrectItsOwnInputButCannotDiscardAnotherInvalidField(bool modal, bool sameField)
    {
        var item = new AccessoryItem("slider-input", "Fictional slider input", "builtin-sprout", 64, 64) { BaseAngleDegrees = 17 };
        AccessorySettingsSession? session = null;
        AccessoryEditorPanel? panel = null;
        AccessoryImageEditorWindow? editor = null;
        FrameworkElement content;
        if (modal)
        {
            editor = new AccessoryImageEditorWindow(item, BuiltinAccessoryImages.Get(item.AssetId)!);
            content = (FrameworkElement)editor.Content; editor.Content = null;
        }
        else
        {
            session = new AccessorySettingsSession(NewRoot()); session.Stage(new([item])); Assert.IsTrue(session.TrySave());
            panel = new AccessoryEditorPanel(); panel.Bind(session); content = panel;
        }
        try
        {
            WithWindow(content, 850, 620, () =>
            {
                var input = Descendants(content).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "配饰尺寸 % 数值");
                Assert.IsTrue(input.Focus()); Pump(); input.Text = "500";
                var slider = Descendants(content).OfType<Slider>().Single(candidate => AutomationProperties.GetName(candidate) == (sameField ? "配饰尺寸 %" : "基础角度 °"));
                slider.Value = sameField ? 185 : 42; Pump();
                var draft = modal ? editor!.Draft : panel!.SelectedItem!;
                if (sameField)
                {
                    Assert.AreEqual(185d, draft.SizePercent);
                    Assert.AreEqual("185", input.Text);
                }
                else
                {
                    Assert.AreEqual(item, draft); Assert.AreEqual("500", input.Text);
                    StringAssert.Contains(modal ? editor!.StatusMessage : panel!.StatusMessage, "配饰尺寸 %");
                }
                if (session is not null) Assert.AreEqual(item, session.SavedConfiguration.Items.Single());
                Assert.IsNull(editor?.Result);
            });
        }
        finally { editor?.Close(); }
    }

    [STATestMethod]
    public void CompactEditorActuallyScrollsAndRendersControlsWithoutHorizontalOverflow()
    {
        var session = new AccessorySettingsSession(NewRoot());
        var panel = new AccessoryEditorPanel { Width = 125, HorizontalAlignment = HorizontalAlignment.Left }; panel.Bind(session);
        panel.Resources["HomeInk"] = Brushes.Black; panel.Resources["HomeSoft"] = Brushes.WhiteSmoke; panel.Resources["HomeLine"] = Brushes.LightGray;
        panel.SetPreviewError("虚构预览错误；应用仅保存配置，显示恢复后此提示才消失。");
        var scroll = new ScrollViewer { Content = panel, Background = Brushes.White, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var window = new Window
        {
            Content = scroll,
            Width = 185,
            Height = 640,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            Title = "Fictional compact accessory editor verification",
        };
        try
        {
            window.Show(); Pump();
            foreach (var group in Descendants(panel).OfType<Expander>()) group.IsExpanded = true;
            Pump();
            var dpi = VisualTreeHelper.GetDpi(panel);
            var roundedWidth = Math.Round(125 * dpi.DpiScaleX) / dpi.DpiScaleX;
            Assert.AreEqual(roundedWidth, panel.ActualWidth, .01, "Face-style layout rounding must resolve to the nearest physical pixel.");
            Assert.AreEqual(Visibility.Visible, scroll.ComputedVerticalScrollBarVisibility);
            Assert.IsTrue(scroll.ScrollableHeight > 0);
            foreach (var control in Descendants(panel).OfType<Control>())
            {
                if (control is not System.Windows.Controls.Button and not TextBox and not Slider and not ComboBox) continue;
                var bounds = control.TransformToAncestor(panel).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                Assert.IsTrue(bounds.Left >= -.01 && bounds.Right <= panel.ActualWidth + .01, $"{control.GetType().Name} escaped the compact editor width: {bounds}.");
            }
            Capture(scroll, "compact-wardrobe-top");
            scroll.ScrollToBottom(); Pump(); Assert.IsTrue(scroll.VerticalOffset > 0);
            Capture(scroll, "compact-wardrobe-actions");
        }
        finally { window.Close(); }
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * image.PixelHeight]; image.CopyPixels(pixels, stride, 0); return pixels;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

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
