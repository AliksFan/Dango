using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Dango.Desktop.Accessories;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryOuterWearingPanelTests
{
    private static (AccessoryEditorPanel Panel, AccessorySettingsSession Session) Fixture()
    {
        var session = new AccessorySettingsSession(Path.Combine(TestRepository.Root, "artifacts", "outer-wearing-panel", Guid.NewGuid().ToString("N")));
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement);
        return (panel, session);
    }

    private static Dictionary<string, T> Controls<T>(DependencyObject root) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Where(c => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(c)))
            .ToDictionary(AutomationProperties.GetName);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }

    [STATestMethod]
    public void OuterFieldsUseCompactActionsNamedThumbnailsAndTwoInitiallyCollapsedGroups()
    {
        var (panel, _) = Fixture();
        var names = Controls<TextBox>(panel); var sliders = Controls<Slider>(panel);
        Assert.IsFalse(names.ContainsKey("配饰名称"));
        Assert.AreEqual(8, sliders.Count);
        Check("尺寸 %", 10, 400, 100); Check("基础角度 °", -180, 180, 0); Check("整体重力响应", 0, 100, 0);
        Assert.IsFalse(sliders.ContainsKey("身体附着 X")); Assert.IsFalse(sliders.ContainsKey("图片根部 X"));
        var expanders = Controls<Expander>(panel);
        CollectionAssert.AreEquivalent(new[] { "连接", "整体摆动" }, expanders.Keys.ToArray());
        Assert.IsTrue(expanders.Values.All(e => !e.IsExpanded));
        foreach (var label in new[] { "尺寸 %", "基础角度 °", "整体重力响应" })
        {
            for (var ancestor = LogicalTreeHelper.GetParent(sliders[label]); ancestor is not null; ancestor = LogicalTreeHelper.GetParent(ancestor))
                Assert.IsFalse(ancestor is Expander, label + " must remain outside collapsed groups.");
        }
        var buttons = Controls<Button>(panel);
        var ordering = (UniformGrid)buttons["团子前"].Parent;
        Assert.AreEqual(1, ordering.Rows); Assert.AreEqual(4, ordering.Columns);
        CollectionAssert.AreEqual(new[] { "团子前", "团子后", "上移", "下移" }, ordering.Children.OfType<Button>().Select(AutomationProperties.GetName).ToArray());
        Assert.IsTrue(ordering.Children.OfType<Button>().All(b => b.Content is StackPanel));
        Assert.IsFalse(Controls<CheckBox>(panel).ContainsKey("佩戴开关"));
        var choices = Controls<ListBox>(panel)["当前已选配饰"];
        Assert.AreEqual(1, choices.Items.Count);
        var viewport = (ScrollViewer)choices.Parent;
        Assert.AreEqual(ScrollBarVisibility.Disabled, viewport.VerticalScrollBarVisibility);
        var selectionLayout = (Grid)viewport.Parent;
        Assert.AreEqual(0, Grid.GetColumn(viewport));
        foreach (var key in new[] { "编辑配饰", "团子上选支点" })
        {
            Assert.AreSame(selectionLayout, ((StackPanel)buttons[key].Parent).Parent);
            Assert.AreEqual(1, Grid.GetColumn((StackPanel)buttons[key].Parent));
            var text = ((StackPanel)buttons[key].Content).Children.OfType<TextBlock>().Single().Text;
            Assert.AreEqual(key == "编辑配饰" ? "编辑" : "选支点", text);
        }
        foreach (var row in choices.Items.Cast<ListBoxItem>())
        {
            var content = (StackPanel)row.Content;
            Assert.IsInstanceOfType<TextBlock>(content.Children[0]);
            Assert.IsInstanceOfType<Grid>(content.Children[1]);
        }

        void Check(string name, double min, double max, double initial)
        {
            var slider = sliders[name]; Assert.AreEqual(min, slider.Minimum); Assert.AreEqual(max, slider.Maximum);
            Assert.AreEqual(initial, slider.Value); slider.ApplyTemplate(); Assert.AreEqual(28d, slider.Height);
        }
    }

    [STATestMethod]
    public void SelectedThumbnailTracksLibrarySelectionAndEditingTargetsTheChosenItem()
    {
        var (panel, session) = Fixture(); var saved = session.SavedConfiguration;
        var choices = Controls<ListBox>(panel)["当前已选配饰"];
        var source = Controls<ListBox>(panel).Where(pair => pair.Key is "已佩戴配饰" or "未佩戴配饰")
            .SelectMany(pair => pair.Value.Items.Cast<ListBoxItem>().Select(row => (List: pair.Value, Row: row)))
            .First(entry => (string)entry.Row.Tag != panel.SelectedItem!.Id);
        var target = source.Row; source.List.SelectedItem = target;
        Assert.AreEqual(1, choices.Items.Count);
        Assert.AreEqual(target.Tag, ((ListBoxItem)choices.Items[0]).Tag);
        Assert.AreEqual((string)target.Tag, panel.SelectedItem!.Id);
        var library = Controls<ListBox>(panel).Values.Where(list => !ReferenceEquals(list, choices));
        Assert.AreEqual(1, library.Count(list => (list.SelectedItem as ListBoxItem)?.Tag as string == panel.SelectedItem.Id));
        var opened = false;
        panel.UnifiedEditor = (draft, _, _, _) =>
        { opened = true; Assert.AreEqual((string)target.Tag, draft.ItemId); return null; };
        Controls<Button>(panel)["编辑配饰"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.IsTrue(opened); Assert.AreEqual(saved, session.SavedConfiguration);
    }

    [STATestMethod]
    public void BufferedWearingNumbersCommitAtomicallyBeforeFollowingChoiceAndInvalidNumbersRemainEditable()
    {
        var (panel, session) = Fixture(); var before = panel.SelectedItem!;
        var input = Controls<TextBox>(panel)["尺寸 % 数值"];
        input.Text = 137.25.ToString(CultureInfo.CurrentCulture);
        panel.Edit(item => item with { Layer = AccessoryLayer.Behind });
        Assert.AreEqual(before with { SizePercent = 137.25, Layer = AccessoryLayer.Behind }, panel.SelectedItem);
        Assert.AreEqual(before, session.SavedConfiguration.Items.Single(i => i.Id == before.Id));
        var committed = session.DraftConfiguration;
        input.Text = "NaN"; panel.ApplyDraft();
        Assert.AreEqual(committed, session.DraftConfiguration); Assert.AreEqual("NaN", input.Text);
        StringAssert.Contains(panel.StatusMessage, "尺寸");
        panel.CancelDraft(); Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        Assert.AreEqual(100d.ToString("0.###", CultureInfo.CurrentCulture), input.Text);
    }

    [STATestMethod]
    public void LockAndLimitDisableOnlyTheirDependentControlsAndPreserveEveryNumber()
    {
        var (panel, _) = Fixture();
        panel.Edit(item => item with { RotationDamping = 17, ReturnStrength = 23, MaxSwingDegrees = 38, GravityInfluence = 42 });
        var sliders = Controls<Slider>(panel); var toggles = Controls<CheckBox>(panel);
        Assert.IsFalse(sliders["旋转阻尼"].IsEnabled); Assert.IsFalse(sliders["整体回正"].IsEnabled);
        Assert.IsFalse(sliders["最大摆角 °"].IsEnabled); Assert.IsTrue(sliders["整体重力响应"].IsEnabled);
        toggles["锁定整体转动"].IsChecked = false; toggles["锁定整体转动"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.IsTrue(sliders["旋转阻尼"].IsEnabled); Assert.IsTrue(sliders["最大摆角 °"].IsEnabled);
        toggles["限制摆角"].IsChecked = false; toggles["限制摆角"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.IsFalse(sliders["最大摆角 °"].IsEnabled); Assert.AreEqual(38d, panel.SelectedItem!.MaxSwingDegrees);
        toggles["锁定整体转动"].IsChecked = true; toggles["锁定整体转动"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.AreEqual(17d, panel.SelectedItem.RotationDamping); Assert.AreEqual(23d, panel.SelectedItem.ReturnStrength);
        Assert.AreEqual(42d, panel.SelectedItem.GravityInfluence); Assert.IsFalse(panel.SelectedItem.LimitSwing);
    }

    [STATestMethod]
    public void OrderButtonsDisableAtSameLayerEdgesAndAnchorEntryInvokesTheRealHostCallback()
    {
        var (panel, session) = Fixture();
        var a = new AccessoryItem("fictional-a", "A", "builtin-sprout", 64, 64) { Order = 0 };
        var b = a with { Id = "fictional-b", Name = "B", Order = 8 };
        var behind = a with { Id = "fictional-behind", Name = "Behind", Layer = AccessoryLayer.Behind, Order = 2 };
        session.Stage(new([a, b, behind])); panel.Bind(session); panel.SelectSection(AccessoryEditorSection.Placement); panel.SelectItem(a.Id);
        var buttons = Controls<Button>(panel);
        Assert.IsTrue(buttons["上移"].IsEnabled); Assert.IsFalse(buttons["下移"].IsEnabled);
        buttons["上移"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(buttons["上移"].IsEnabled); Assert.IsTrue(buttons["下移"].IsEnabled);
        Assert.AreEqual(behind, session.DraftConfiguration.Items.Single(i => i.Id == behind.Id));
        var selected = panel.SelectedItem!; var requests = 0; panel.BodyAnchorPickRequested = () => requests++;
        buttons["团子上选支点"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(1, requests); Assert.AreEqual(selected.ImagePivotX, panel.SelectedItem!.ImagePivotX);
        panel.SetBodyAnchor(new(.23, .61)); Assert.AreEqual(.23, panel.SelectedItem!.BodyAnchorX); Assert.AreEqual(.61, panel.SelectedItem.BodyAnchorY);
        Assert.AreEqual(selected.ImagePivotY, panel.SelectedItem.ImagePivotY);
        var chosen = Controls<ListBox>(panel)["当前已选配饰"].Items.Cast<ListBoxItem>().Single(row => (string)row.Tag == selected.Id);
        chosen.ContextMenu!.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        ((MenuItem)chosen.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.IsFalse(panel.SelectedItem.IsEquipped);
    }
}
