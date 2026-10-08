using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Real WPF layout and hit targets in an owned Home view; never starts the product app or native solver.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AccessoryHomeUiTests
{
    private static readonly string[] SaveActions = ["复位当前配饰", "取消", "应用"];
    private static readonly (AccessoryEditorSection Section, string Name)[] SectionActions =
    [
        (AccessoryEditorSection.Library, "配饰：配饰库"),
        (AccessoryEditorSection.Placement, "配饰：图像与位置"),
        (AccessoryEditorSection.Connection, "配饰：柔性与连接"),
        (AccessoryEditorSection.Rotation, "配饰：旋转与重力"),
    ];

    [STATestMethod]
    [DataRow(1024, 640)]
    [DataRow(640, 420)]
    [DataRow(640, 360)]
    [DataRow(480, 800)]
    [DataRow(360, 640)]
    public void WardrobeMirrorsFaceWithFixedToolsAndUnchangedDangoGeometry(int width, int height)
    {
        using var fixture = CreateFixture();
        var home = fixture.Home;
        var size = new Size(width, height);
        Layout(home, size);
        var closed = Bounds(home, "PetPreview");
        var navigation = Bounds(home, "TopNavigation");

        home.ShowEditor(HomeEditor.Face);
        Layout(home, size);
        var face = Bounds(home, "PetPreview");
        AssertPreviewGeometry(closed, face, "face");

        home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, size);
        Capture(home, $"wardrobe-{width}x{height}");
        var wardrobe = Bounds(home, "PetPreview");
        AssertPreviewGeometry(closed, wardrobe, "wardrobe");
        Assert.AreEqual(navigation, Bounds(home, "TopNavigation"), "The editor must retain the fixed home navigation.");
        Assert.AreEqual(Visibility.Collapsed, Element<FrameworkElement>(home, "Launchers").Visibility);
        Assert.AreEqual(Visibility.Hidden, Element<FrameworkElement>(home, "FaceEditorTools").Visibility);
        Assert.AreEqual(Visibility.Collapsed, Element<FrameworkElement>(home, "EditorHeading").Visibility,
            "The wardrobe must release its old parameter-column close header, as the face editor does.");

        if (height <= width * 1.2)
        {
            Assert.IsTrue(face.Left < closed.Left - 20, "Face parameters enter on the right and shift the body left.");
            Assert.IsTrue(wardrobe.Left > closed.Left + 20, "Wardrobe parameters must mirror face and enter on the left.");
            Assert.IsTrue(Bounds(home, "OptionsBody").Right + 3 <= wardrobe.Left,
                "Wardrobe parameter controls must remain left of the body.");
        }
        else
        {
            Assert.IsTrue(Bounds(home, "EditorPanel").Top >= wardrobe.Bottom + 8,
                "Portrait parameters must remain below the preview.");
        }

        Panel(home).SetBodyAnchor(new Point(.25, .4));
        Layout(home, size);
        Assert.IsTrue(fixture.Accessories.IsDirty, "This gate must exercise a pending save, not only disabled buttons.");
        AssertFixedTools(home);
        AssertIconRow(home);
        AssertLeftSectionTabs(home);
        foreach (var action in SaveActions) AssertReachable(home, SaveButton(home, action), action);
        AssertReachable(home, Element<Button>(home, "AccessoryCloseEditorButton"), "wardrobe close");
        StringAssert.Contains(Element<TextBlock>(home, "AccessoryPreviewNotice").Text, "应用后保存");
        Capture(home, $"wardrobe-{width}x{height}");

        var selectedId = Panel(home).SelectedItem!.Id;
        var otherItems = fixture.Accessories.DraftConfiguration.Items.Where(item => item.Id != selectedId).ToArray();
        SaveButton(home, "复位当前配饰").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(home, size);
        Assert.AreEqual(.5, Panel(home).SelectedItem!.BodyAnchorX, 1e-9, "The footer reset must execute the selected item's real reset action.");
        Assert.AreEqual(0, Panel(home).SelectedItem!.BodyAnchorY, 1e-9);
        CollectionAssert.AreEqual(otherItems, fixture.Accessories.DraftConfiguration.Items.Where(item => item.Id != selectedId).ToArray(),
            "Resetting one accessory must preserve every other library entry.");
        Assert.IsFalse(File.Exists(fixture.Manifest), "Reset must remain a preview until explicit application.");

        Click(home, "AccessoryCloseEditorButton");
        Layout(home, size);
        Assert.AreEqual(closed, Bounds(home, "PetPreview"), "Closing returns to exactly the home body geometry.");
        Assert.IsFalse(fixture.Accessories.IsDirty);
        Assert.IsFalse(File.Exists(fixture.Manifest), "Closing the editor must cancel rather than silently save.");
    }

    [STATestMethod]
    // These owned component-harness sizes exercise actual compact/portrait overflow.
    // The formal Window minimum is 820 x 560; 1024 x 560 naturally fits and is verified separately.
    [DataRow(640, 420)]
    [DataRow(480, 800)]
    public void ScrollingParametersCannotMoveOrHideThePreviewFooter(int width, int height)
    {
        using var fixture = CreateFixture();
        var home = fixture.Home;
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsTrue(Panel(home).SelectSection(AccessoryEditorSection.Placement));
        Panel(home).SetBodyAnchor(new Point(.31, .47));
        Layout(home, size);
        var scroll = Element<ScrollViewer>(home, "OptionsBody");
        Assert.IsTrue(scroll.ScrollableHeight > 0, "This regression must exercise actual parameter overflow.");
        Assert.AreEqual(Visibility.Visible, scroll.ComputedVerticalScrollBarVisibility);
        Assert.AreEqual(0, scroll.ScrollableWidth, .01, "Parameters must fit horizontally.");
        var fixedNames = new[] { "PetPreview", "AccessoryPreviewNotice", "AccessoryCloseEditorButton", "AccessorySaveActionsSlot", "AccessoryActionToolsSlot" };
        var fixedBounds = fixedNames.ToDictionary(name => name, name => Bounds(home, name));
        var draft = fixture.Accessories.DraftConfiguration;

        scroll.ScrollToBottom();
        Layout(home, size);
        Capture(home, $"wardrobe-scrolled-{width}x{height}");

        Assert.IsTrue(scroll.VerticalOffset > 0, "The test must have reached a later parameter section.");
        foreach (var name in fixedNames)
            Assert.AreEqual(fixedBounds[name], Bounds(home, name), name + " must not scroll with the parameter body.");
        AssertFixedTools(home);
        AssertIconRow(home);
        AssertLeftSectionTabs(home);
        foreach (var action in SaveActions) AssertReachable(home, SaveButton(home, action), action + " after parameter scrolling");
        AssertReachable(home, Element<Button>(home, "AccessoryCloseEditorButton"), "wardrobe close after scrolling");
        Assert.AreEqual(draft, fixture.Accessories.DraftConfiguration);
        Assert.IsTrue(fixture.Accessories.IsDirty);
        Assert.IsFalse(File.Exists(fixture.Manifest), "Parameter scrolling must not commit a draft.");
        Capture(home, $"wardrobe-scrolled-{width}x{height}");
    }

    [STATestMethod]
    [DataRow("close")]
    [DataRow("cancel")]
    [DataRow("face")]
    [DataRow("settings")]
    [DataRow("hide")]
    public void LeavingWardrobeCancelsItsDraftAndPreservesBothSavedModules(string exit)
    {
        using var fixture = CreateFixture();
        Assert.IsTrue(fixture.Accessories.TrySave());
        fixture.Appearance.StageBodyPreview(new(91, 137, 181));
        Assert.IsTrue(fixture.Appearance.TrySave());
        var accessoryBytes = File.ReadAllBytes(fixture.Manifest);
        var appearanceBytes = File.ReadAllBytes(fixture.AppearancePath);
        var savedAccessories = fixture.Accessories.SavedConfiguration;
        var savedAppearance = fixture.Appearance.SavedAppearance;
        var home = fixture.Home;
        var size = new Size(640, 420);
        Layout(home, size);

        home.ShowEditor(HomeEditor.Face);
        Click(home, "PeachButton");
        Assert.IsTrue(fixture.Appearance.IsDirty, "The face side of the cancellation boundary must contain a real draft.");
        home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, size);
        Assert.IsFalse(fixture.Appearance.IsDirty, "Switching to wardrobe must discard the unsaved face draft.");
        Panel(home).SetBodyAnchor(new Point(.23, .68));
        Layout(home, size);
        Assert.IsTrue(fixture.Accessories.IsDirty);
        AssertReachable(home, Element<Button>(home, "AccessoryCloseEditorButton"), "wardrobe close");
        AssertReachable(home, SaveButton(home, "取消"), "cancel");

        switch (exit)
        {
            case "close": Click(home, "AccessoryCloseEditorButton"); break;
            case "cancel": SaveButton(home, "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
            case "face": home.ShowEditor(HomeEditor.Face); break;
            case "settings": home.ShowSettingsPage(); break;
            case "hide": home.Visibility = Visibility.Hidden; break;
            default: Assert.Fail("Unknown cancellation path."); break;
        }
        Layout(home, size);

        Assert.IsFalse(fixture.Accessories.IsDirty, exit + " must cancel the wardrobe draft.");
        Assert.AreEqual(savedAccessories, fixture.Accessories.DraftConfiguration);
        Assert.IsFalse(fixture.Appearance.IsDirty);
        Assert.AreEqual(savedAppearance, fixture.Appearance.DraftAppearance);
        CollectionAssert.AreEqual(accessoryBytes, File.ReadAllBytes(fixture.Manifest), exit + " must preserve saved accessory bytes.");
        CollectionAssert.AreEqual(appearanceBytes, File.ReadAllBytes(fixture.AppearancePath), exit + " must preserve saved face bytes.");
        Assert.AreEqual(savedAccessories, new AccessorySettingsSession(fixture.Root).SavedConfiguration);
        Assert.AreEqual(savedAppearance, new AppearanceSettingsSession(fixture.AppearancePath).SavedAppearance);
        if (exit != "hide")
        {
            var tools = Element<FrameworkElement>(home, "AccessoryEditorTools");
            Assert.IsFalse(tools.IsEnabled || tools.IsHitTestVisible, "Closed wardrobe tools must not intercept input.");
            Assert.AreEqual(Visibility.Hidden, tools.Visibility);
        }
    }

    [STATestMethod]
    // Keep real scrolling in this component harness; do not claim its compact size is a formal Window size.
    [DataRow(640, 420)]
    [DataRow(480, 800)]
    public void TheReachableFooterActuallyAppliesAndReloadsAfterParameterScrolling(int width, int height)
    {
        using var fixture = CreateFixture();
        Assert.IsTrue(fixture.Accessories.TrySave());
        var home = fixture.Home;
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsTrue(Panel(home).SelectSection(AccessoryEditorSection.Placement));
        Layout(home, size);
        Assert.IsFalse(SaveButton(home, "应用").IsEnabled, "A clean current-format library must not advertise a pending save.");
        Panel(home).SetBodyAnchor(new Point(.73, .16));
        var editedId = Panel(home).SelectedItem!.Id;
        Layout(home, size);
        var scroll = Element<ScrollViewer>(home, "OptionsBody");
        Assert.IsTrue(scroll.ScrollableHeight > 0, "Applying after scrolling must exercise actual placement overflow.");
        scroll.ScrollToBottom();
        Layout(home, size);
        Assert.IsTrue(scroll.VerticalOffset > 0, "Apply must remain reachable after actual parameter scrolling.");
        AssertReachable(home, SaveButton(home, "应用"), "apply after scrolling");
        SaveButton(home, "应用").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(home, size);

        Assert.IsFalse(fixture.Accessories.IsDirty);
        Assert.IsFalse(SaveButton(home, "应用").IsEnabled);
        var reloaded = new AccessorySettingsSession(fixture.Root).SavedConfiguration;
        Assert.HasCount(3, reloaded.Items, "Applying must preserve all three existing built-in entries.");
        Assert.AreEqual(.73, reloaded.Items.Single(item => item.Id == editedId).BodyAnchorX, 1e-9);
        Assert.AreEqual(.16, reloaded.Items.Single(item => item.Id == editedId).BodyAnchorY, 1e-9);
        Assert.AreEqual(fixture.Accessories.SavedConfiguration, reloaded);
        Assert.IsFalse(File.Exists(fixture.AppearancePath), "Accessory application must not write a face draft.");
        Capture(home, $"wardrobe-applied-{width}x{height}");
    }

    [STATestMethod]
    public void PlacementFitsAt1024By560AndItsFixedToolsRemainReachableAndApply()
    {
        using var fixture = CreateFixture();
        var home = fixture.Home; var size = new Size(1024, 560);
        Layout(home, size); var closed = Bounds(home, "PetPreview");
        home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsTrue(Panel(home).SelectSection(AccessoryEditorSection.Placement));
        Panel(home).SetBodyAnchor(new Point(.73, .16)); Layout(home, size);
        var scroll = Element<ScrollViewer>(home, "OptionsBody");
        Assert.AreEqual(0d, scroll.ScrollableHeight, .01, "Removing the wardrobe actions and wear checkbox allows placement to naturally fit at this supported wide size.");
        Assert.AreEqual(Visibility.Collapsed, scroll.ComputedVerticalScrollBarVisibility);
        Assert.AreEqual(0d, scroll.ScrollableWidth, .01);
        AssertPreviewGeometry(closed, Bounds(home, "PetPreview"), "wardrobe fit");
        AssertFixedTools(home); AssertIconRow(home); AssertLeftSectionTabs(home);
        foreach (var action in SaveActions) AssertReachable(home, SaveButton(home, action), action + " in naturally fitting placement");
        AssertReachable(home, Element<Button>(home, "AccessoryCloseEditorButton"), "wardrobe close in naturally fitting placement");
        var draft = fixture.Accessories.DraftConfiguration;
        scroll.ScrollToBottom(); Layout(home, size);
        Assert.AreEqual(0d, scroll.VerticalOffset, .01, "A fitting parameter section must not invent an overflow path.");
        Assert.AreEqual(draft, fixture.Accessories.DraftConfiguration);
        Assert.IsFalse(File.Exists(fixture.Manifest), "Layout and scroll attempts must not save the preview.");
        SaveButton(home, "应用").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(home, size);
        Assert.AreEqual(draft, new AccessorySettingsSession(fixture.Root).SavedConfiguration);
        Assert.IsFalse(fixture.Accessories.IsDirty);
        Assert.IsFalse(File.Exists(fixture.AppearancePath));
        Capture(home, "wardrobe-placement-fit-1024x560");
    }

    [STATestMethod]
    [DataRow(1024, 640)]
    [DataRow(640, 420)]
    [DataRow(480, 800)]
    public void LibraryHasFourActualImageCardsPerRowAndLeftTabsPreserveTheDraft(int width, int height)
    {
        using var fixture = CreateFixture();
        PopulateLibrary(fixture);
        var home = fixture.Home;
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, size);
        Assert.AreEqual(AccessoryEditorSection.Library, Panel(home).SelectedSection, "The wardrobe must initially show the image library.");
        AssertFourColumnLibrary(home);
        AssertLeftSectionTabs(home);

        Panel(home).SetBodyAnchor(new Point(.29, .63));
        var selectedId = Panel(home).SelectedItem!.Id;
        var draft = fixture.Accessories.DraftConfiguration;
        foreach (var (section, name) in SectionActions.Reverse())
        {
            SectionButton(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(home, size);
            Assert.AreEqual(section, Panel(home).SelectedSection, name + " must select its actual section.");
            Assert.AreEqual(selectedId, Panel(home).SelectedItem!.Id, "Section navigation must retain the selected accessory.");
            Assert.AreEqual(draft, fixture.Accessories.DraftConfiguration, "Section navigation must not reset or commit parameters.");
            Assert.AreEqual("已选", AutomationProperties.GetItemStatus(SectionButton(home, name)));
            Assert.HasCount(1, SectionActions.Where(action => AutomationProperties.GetItemStatus(SectionButton(home, action.Name)) == "已选").ToArray(),
                "Exactly one section must expose the selected state.");
            AssertLeftSectionTabs(home);
        }
        AssertFourColumnLibrary(home);
        Assert.IsFalse(File.Exists(fixture.Manifest), "The gallery and section tabs must not save the draft.");
        Capture(home, $"wardrobe-library-{width}x{height}");
    }

    [STATestMethod]
    [DataRow("three-columns")]
    [DataRow("right-side-tabs")]
    [DataRow("oversized-tab")]
    public void LibraryAndSectionLayoutGatesRejectBrokenCompositions(string mutation)
    {
        using var fixture = CreateFixture();
        PopulateLibrary(fixture);
        var home = fixture.Home;
        var size = new Size(1024, 640);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, size);
        AssertFourColumnLibrary(home);
        AssertLeftSectionTabs(home);

        switch (mutation)
        {
            case "three-columns":
                var cells = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));
                cells.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, 3);
                Library(home).ItemsPanel = new ItemsPanelTemplate(cells);
                Layout(home, size);
                Assert.ThrowsExactly<AssertFailedException>(() => AssertFourColumnLibrary(home));
                break;
            case "right-side-tabs":
                Grid.SetColumn(Element<ContentControl>(home, "AccessorySectionTabsSlot"), 2);
                Layout(home, size);
                Assert.ThrowsExactly<AssertFailedException>(() => AssertLeftSectionTabs(home));
                break;
            case "oversized-tab":
                SectionButton(home, "配饰：图像与位置").Width = 56;
                Layout(home, size);
                Assert.ThrowsExactly<AssertFailedException>(() => AssertLeftSectionTabs(home));
                break;
            default: Assert.Fail("Unknown broken library composition."); break;
        }
    }

    [STATestMethod]
    [DataRow("scrolling-footer")]
    [DataRow("oversized-icon")]
    [DataRow("body-scale")]
    public void LayoutGatesRejectRepresentativeBrokenCompositions(string mutation)
    {
        using var fixture = CreateFixture();
        var home = fixture.Home;
        var size = new Size(1024, 640);
        Layout(home, size);
        var closed = Bounds(home, "PetPreview");
        home.ShowEditor(HomeEditor.Wardrobe);
        Panel(home).SetBodyAnchor(new Point(.22, .45));
        Layout(home, size);
        AssertFixedTools(home);
        AssertIconRow(home);
        AssertPreviewGeometry(closed, Bounds(home, "PetPreview"), "wardrobe");

        switch (mutation)
        {
            case "scrolling-footer":
                var slot = Element<ContentControl>(home, "AccessorySaveActionsSlot");
                var footer = (FrameworkElement)slot.Content;
                slot.Content = null;
                Panel(home).Children.Add(footer);
                Layout(home, size);
                Assert.ThrowsExactly<AssertFailedException>(() => AssertFixedTools(home));
                break;
            case "oversized-icon":
                SaveButton(home, "应用").Width = 56;
                Layout(home, size);
                Assert.ThrowsExactly<AssertFailedException>(() => AssertIconRow(home));
                break;
            case "body-scale":
                Element<FrameworkElement>(home, "PetPreview").RenderTransform = new ScaleTransform(1, 1.2);
                Layout(home, size);
                Assert.ThrowsExactly<AssertFailedException>(() => AssertPreviewGeometry(closed, Bounds(home, "PetPreview"), "wardrobe"));
                break;
            default: Assert.Fail("Unknown broken composition."); break;
        }
    }

    private static HomeFixture CreateFixture()
    {
        var root = System.IO.Path.Combine(TestRepository.Root, "artifacts", "accessory-ui-home-harness", "cases", Guid.NewGuid().ToString("N"));
        var accessories = new AccessorySettingsSession(root);
        var appearancePath = System.IO.Path.Combine(root, "appearance.json");
        var appearance = new AppearanceSettingsSession(appearancePath);
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAccessories(accessories);
        home.BindAppearance(appearance);
        var window = new Window
        {
            Content = home,
            Width = 1024,
            Height = 640,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Fictional accessory Home layout verification",
        };
        window.Show();
        return new(home, accessories, appearance, root, AccessorySettingsStore.GetManifestPath(root), appearancePath, window);
    }

    private static void AssertFixedTools(ProductHomeView home)
    {
        var tools = Element<FrameworkElement>(home, "AccessoryEditorTools");
        var slot = Element<ContentControl>(home, "AccessorySaveActionsSlot");
        var options = Element<ScrollViewer>(home, "OptionsBody");
        Assert.IsNotNull(slot.Content, "The fixed preview footer must own its save actions.");
        Assert.IsFalse(options.IsAncestorOf(slot), "The footer must live outside the scrolling parameter column.");
        Assert.IsTrue(tools.IsAncestorOf(slot), "The footer must belong to the wardrobe preview tools.");
        Assert.AreEqual(Visibility.Visible, tools.Visibility);
        Assert.IsTrue(tools.IsEnabled && tools.IsHitTestVisible);
        var viewport = Bounds(home, "HomeContent");
        var preview = Bounds(home, "PetPreview");
        var shadow = Bounds(home, "PetShadow");
        var footer = Bounds(home, slot);
        var notice = Bounds(home, "AccessoryPreviewNotice");
        var close = Bounds(home, "AccessoryCloseEditorButton");
        AssertInside(viewport, footer, "fixed footer within the visible home viewport");
        AssertInside(viewport, close, "close within the visible home viewport");
        AssertInside(viewport, notice, "preview notice within the visible home viewport");
        Assert.IsTrue(footer.Top >= shadow.Bottom + 3, $"Footer must stay below the ground shadow; footer {footer}, shadow {shadow}.");
        Assert.IsTrue(notice.Bottom <= preview.Top + .5, "The preview instruction must stay above the body.");
        AssertNoOverlap(notice, close, "notice and close");
        AssertNoOverlap(preview, close, "body and close");
        AssertNoOverlap(preview, footer, "body and footer");
        AssertNoOverlap(Bounds(home, "OptionsBody"), Bounds(home, tools), "parameters and fixed preview tools");
        AssertNoOverlap(Bounds(home, "OptionsBody"), footer, "parameters and footer");
        if (home.ActualHeight > home.ActualWidth * 1.2)
        {
            Assert.AreEqual(0, Grid.GetRow(tools));
            Assert.AreEqual(1, Grid.GetRowSpan(tools));
            Assert.AreEqual(preview.Width, footer.Width, 1, "Portrait saving area must use the body preview width.");
            Assert.AreEqual(preview.Left, footer.Left, 1, "Portrait saving area must share the body horizontal anchor.");
            Assert.IsTrue(footer.Bottom + 3 <= Bounds(home, "EditorPanel").Top,
                "Portrait save controls must stay above the lower parameter area.");
        }
    }

    private static void AssertIconRow(ProductHomeView home)
    {
        var footer = Element<ContentControl>(home, "AccessorySaveActionsSlot");
        var row = SaveActions.Select(action => SaveButton(home, action)).ToArray();
        var first = Bounds(home, row[0]);
        foreach (var button in row)
        {
            var action = AutomationProperties.GetName(button);
            var bounds = Bounds(home, button);
            Assert.AreEqual(32, bounds.Width, .01, action + " must use the face editor's 32-DIP target width.");
            Assert.AreEqual(32, bounds.Height, .01, action + " must use the face editor's 32-DIP target height.");
            Assert.AreEqual(first.Top, bounds.Top, .5, "The saving icons must share one horizontal row.");
            var paths = Descendants<System.Windows.Shapes.Path>(button).ToArray();
            Assert.HasCount(1, paths, action + " must draw one linear vector icon.");
            Assert.IsNotNull(paths[0].Data);
            Assert.IsNotNull(paths[0].Stroke);
            Assert.IsTrue(paths[0].StrokeThickness > 0);
            Assert.IsTrue(paths[0].Fill is null || Equals(paths[0].Fill, Brushes.Transparent), action + " must be a line icon.");
            Assert.IsFalse(button.Content is string, action + " must not revert to a text button.");
            Assert.IsFalse(Descendants<TextBlock>(button).Any(text => text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text)));
            Assert.IsFalse(string.IsNullOrWhiteSpace(button.ToolTip?.ToString()), action + " must retain an understandable tooltip.");
        }
        for (var index = 0; index < row.Length - 1; index++)
            Assert.AreEqual(8, Bounds(home, row[index + 1]).Left - Bounds(home, row[index]).Right, 1,
                "Reset, cancel and apply must preserve the face editor's 8-DIP gaps and order.");
        Assert.AreEqual(Bounds(home, footer).Right, Bounds(home, row[^1]).Right, 1,
            "Apply must align to the right edge of the fixed footer.");
        Assert.AreEqual("Primary", row[^1].Tag);
        AssertRightActionTools(home);
    }

    private static void AssertRightActionTools(ProductHomeView home)
    {
        var slot = Element<ContentControl>(home, "AccessoryActionToolsSlot");
        Assert.AreSame(Panel(home).ActionTools, slot.Content);
        Assert.IsFalse(Element<ScrollViewer>(home, "OptionsBody").IsAncestorOf(slot), "The action column must stay outside parameter scrolling.");
        var tools = Descendants<Button>(slot).ToArray();
        CollectionAssert.AreEqual(new[] { "创建新配饰", "复制配饰", "删除配饰" }, tools.Select(AutomationProperties.GetName).ToArray());
        var preview = Bounds(home, "PetPreview");
        var first = Bounds(home, tools[0]);
        var leftFirst = Bounds(home, SectionButton(home, "配饰：配饰库"));
        Assert.AreEqual(leftFirst.Top, first.Top, 1, "The action column must align with the first left section icon.");
        for (var index = 0; index < tools.Length; index++)
        {
            var bounds = Bounds(home, tools[index]); var name = AutomationProperties.GetName(tools[index]);
            Assert.AreEqual(32d, bounds.Width, .01); Assert.AreEqual(32d, bounds.Height, .01);
            Assert.AreEqual(first.Left, bounds.Left, .5);
            Assert.IsTrue(bounds.Left >= preview.Right + 3, name + " must remain right of the Dango body.");
            if (index > 0) Assert.AreEqual(8d, bounds.Top - Bounds(home, tools[index - 1]).Bottom, 1);
            Assert.HasCount(1, Descendants<System.Windows.Shapes.Path>(tools[index]).ToArray());
            Assert.IsFalse(string.IsNullOrWhiteSpace(tools[index].ToolTip?.ToString()));
            AssertReachable(home, tools[index], name);
        }
    }

    private static void PopulateLibrary(HomeFixture fixture)
    {
        var original = fixture.Accessories.DraftConfiguration.Items[0].Id;
        while (fixture.Accessories.DraftConfiguration.Items.Count < 8) fixture.Accessories.Duplicate(original);
        fixture.Accessories.Stage(new(fixture.Accessories.DraftConfiguration.Items.Select((item, index) =>
            item with { Name = $"虚构图库配饰 {index + 1}" })));
        // This is external fixture initialization, not a user editing refresh:
        // discard the previously bound name buffer when adopting the staged data.
        Panel(fixture.Home).DiscardInputsAndRefresh();
    }

    private static ListBox Library(ProductHomeView home, bool equipped = true) => Descendants<ListBox>(Panel(home))
        .Single(list => AutomationProperties.GetName(list) == (equipped ? "已佩戴配饰" : "未佩戴配饰"));

    private static void AssertFourColumnLibrary(ProductHomeView home)
    {
        var list = Library(home);
        var cards = list.Items.Cast<ListBoxItem>().ToArray();
        var unequipped = Library(home, false).Items.Cast<ListBoxItem>().ToArray();
        Assert.AreEqual(8, cards.Length + unequipped.Length);
        foreach (var card in cards) StringAssert.StartsWith(AutomationProperties.GetItemStatus(card), "已佩戴");
        foreach (var card in unequipped) StringAssert.StartsWith(AutomationProperties.GetItemStatus(card), "未佩戴");
        Assert.IsFalse(Descendants<TextBlock>(Panel(home)).Any(text => text.Text == "配饰库"), "The library must use section headings instead of repeating its title.");
        Assert.IsTrue(cards.Length > 4, "The grid gate needs more than one row of real library entries.");
        Assert.AreEqual(AccessoryEditorSection.Library, Panel(home).SelectedSection);
        var first = Bounds(home, cards[0]);
        for (var index = 0; index < cards.Length; index++)
        {
            var card = cards[index];
            var bounds = Bounds(home, card);
            Assert.AreEqual(first.Top + (index / 4) * (Bounds(home, cards[4]).Top - first.Top), bounds.Top, 1,
                "Library images must wrap after exactly four cards.");
            Assert.AreEqual(Bounds(home, cards[index % 4]).Left, bounds.Left, 1, "Rows must share the same four image columns.");
            AssertInside(Bounds(home, list), bounds, "visible gallery card " + index);
            var image = Descendants<Image>(card).Single();
            var thumbnail = Assert.IsInstanceOfType<BitmapSource>(image.Source,
                "Every visible library card must show its actual image, rather than a text placeholder.");
            Assert.IsTrue(thumbnail.PixelWidth > 0 && thumbnail.PixelHeight > 0 &&
                Math.Max(thumbnail.PixelWidth, thumbnail.PixelHeight) <= AccessoryEditorPanel.ThumbnailEdge,
                "Gallery images must use bounded thumbnails of the real asset.");
            AssertInside(bounds, Bounds(home, image), "image within gallery card " + index);
            Assert.IsTrue(bounds.Height <= 70, "Gallery cards must retain their natural icon-row height.");
            Assert.IsFalse(card.Content is string, "Library rows must contain image cards.");
            StringAssert.Contains(AutomationProperties.GetName(card), (string)card.ToolTip,
                "Image cards must retain their accessory name for automation and accessibility.");
        }
        Assert.IsTrue(Bounds(home, cards[4]).Top >= first.Bottom - .5, "The fifth image must occupy a later row.");
        for (var index = 0; index < 3; index++)
            Assert.IsTrue(Bounds(home, cards[index + 1]).Left >= Bounds(home, cards[index]).Right - .5,
                "The first four image cards must appear side by side without overlap.");
    }

    private static Button SectionButton(ProductHomeView home, string name) =>
        Descendants<Button>(Element<ContentControl>(home, "AccessorySectionTabsSlot"))
            .Single(button => AutomationProperties.GetName(button) == name);

    private static void AssertLeftSectionTabs(ProductHomeView home)
    {
        var slot = Element<ContentControl>(home, "AccessorySectionTabsSlot");
        Assert.AreSame(Panel(home).SectionTabs, slot.Content, "The preview must host the panel's real section controls.");
        Assert.IsFalse(Element<ScrollViewer>(home, "OptionsBody").IsAncestorOf(slot), "Section tabs must remain outside parameter scrolling.");
        var buttons = SectionActions.Select(action => SectionButton(home, action.Name)).ToArray();
        Assert.HasCount(4, Descendants<Button>(slot).ToArray());
        var preview = Bounds(home, "PetPreview");
        var first = Bounds(home, buttons[0]);
        for (var index = 0; index < buttons.Length; index++)
        {
            var bounds = Bounds(home, buttons[index]);
            var name = AutomationProperties.GetName(buttons[index]);
            Assert.AreEqual(32, bounds.Width, .01, name + " must use the face editor's 32-DIP target.");
            Assert.AreEqual(32, bounds.Height, .01);
            Assert.AreEqual(first.Left, bounds.Left, .5, "Section icons must form one vertical column.");
            Assert.IsTrue(bounds.Right + 3 <= preview.Left, name + " must remain left of the Dango preview.");
            if (index > 0) Assert.AreEqual(8, bounds.Top - Bounds(home, buttons[index - 1]).Bottom, 1,
                "Section tabs must retain the face editor's 8-DIP gaps.");
            var path = Descendants<System.Windows.Shapes.Path>(buttons[index]).Single();
            Assert.IsNotNull(path.Data); Assert.IsNotNull(path.Stroke);
            Assert.IsTrue(path.StrokeThickness > 0);
            Assert.IsTrue(path.Fill is null || Equals(path.Fill, Brushes.Transparent), "Section icons must use line artwork.");
            AssertReachable(home, buttons[index], name);
        }
    }

    private static void AssertPreviewGeometry(Rect baseline, Rect actual, string editor)
    {
        Assert.AreEqual(baseline.Top, actual.Top, .5, editor + " must retain CommonPreviewTop.");
        Assert.AreEqual(baseline.Width, actual.Width, .5, editor + " must not change body width.");
        Assert.AreEqual(baseline.Height, actual.Height, .5, editor + " must not change body height.");
    }

    private static void AssertReachable(ProductHomeView home, Button button, string action)
    {
        Assert.AreEqual(Visibility.Visible, button.Visibility, action + " must be visible.");
        Assert.IsTrue(button.IsEnabled && button.IsHitTestVisible, action + " must accept input.");
        var bounds = Bounds(home, button);
        AssertInside(new Rect(home.RenderSize), bounds, action);
        var hit = home.InputHitTest(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)) as DependencyObject;
        Assert.IsNotNull(hit, action + " must have an actual input hit target.");
        Assert.IsTrue(ReferenceEquals(hit, button) || button.IsAncestorOf(hit),
            action + " center must reach its button rather than an overlapping parameter or preview surface.");
    }

    private static T Element<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new AssertFailedException("Missing compiled wardrobe control: " + name);

    private static AccessoryEditorPanel Panel(ProductHomeView home) =>
        Element<StackPanel>(home, "WardrobeOptions").Children.OfType<AccessoryEditorPanel>().Single();

    private static Button SaveButton(ProductHomeView home, string action) =>
        Descendants<Button>(Element<ContentControl>(home, "AccessorySaveActionsSlot"))
            .Single(button => AutomationProperties.GetName(button) == action);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants<T>(child)) yield return nested;
    }

    private static void Click(ProductHomeView home, string name) =>
        Element<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Layout(ProductHomeView home, Size size)
    {
        if (Window.GetWindow(home) is { } window)
        {
            window.Width = size.Width;
            window.Height = size.Height;
            window.UpdateLayout();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        home.Measure(size);
        home.Arrange(new Rect(size));
        home.UpdateLayout();
    }

    private static Rect Bounds(ProductHomeView home, string name) => Bounds(home, Element<FrameworkElement>(home, name));

    private static Rect Bounds(ProductHomeView home, FrameworkElement element) =>
        element.TransformToAncestor(home).TransformBounds(new Rect(element.RenderSize));

    private static void AssertInside(Rect area, Rect child, string label) =>
        Assert.IsTrue(child.Width > 0 && child.Height > 0 && child.Left >= area.Left - .5 && child.Top >= area.Top - .5 &&
            child.Right <= area.Right + .5 && child.Bottom <= area.Bottom + .5,
            $"{label} must remain within {area}; actual {child}.");

    private static void AssertNoOverlap(Rect first, Rect second, string label)
    {
        var overlap = Rect.Intersect(first, second);
        Assert.IsTrue(overlap.IsEmpty || overlap.Width <= .5 || overlap.Height <= .5,
            $"{label} must not overlap; first {first}, second {second}.");
    }

    private static void Capture(FrameworkElement control, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(control.ActualWidth), (int)Math.Ceiling(control.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var root = System.IO.Path.Combine(TestRepository.Root, "artifacts", "accessory-ui-home-harness", "screenshots");
        Directory.CreateDirectory(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(root, name + ".png"));
        encoder.Save(stream);
    }

    private sealed record HomeFixture(ProductHomeView Home, AccessorySettingsSession Accessories,
        AppearanceSettingsSession Appearance, string Root, string Manifest, string AppearancePath, Window Window) : IDisposable
    {
        public void Dispose() => Window.Close();
    }
}
