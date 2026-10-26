using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DangoSlotHomeTests
{
    private static readonly Guid First = Guid.Parse("04ec85bc-671c-416a-a680-128e8676dafe");
    private static readonly Guid Third = Guid.Parse("1e8ecc40-58ac-4075-bc2f-85036f8f354b");
    private static DangoSlotView[] Slots() => [new(1, First, "米团", true), new(2, null, null, false), new(3, Third, "豆团", false)];
    private static T Get<T>(ProductHomeView view, string name) where T : class => (T)view.FindName(name);
    private static ProductHomeView Home()
    {
        var view = new ProductHomeView { ReduceMotion = true };
        view.ConfigureDangoSlots(Slots(), First, "米团", false);
        return view;
    }
    private static void Layout(ProductHomeView view, int width, int height)
    {
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
    }
    private static Rect Bounds(ProductHomeView view, string name)
    {
        var item = Get<FrameworkElement>(view, name);
        return item.TransformToAncestor(view).TransformBounds(new Rect(item.RenderSize));
    }
    [STATestMethod]
    [DataRow(960, 540)]
    [DataRow(480, 800)]
    [DataRow(640, 360)]
    public void FixedSlotsKeepHoleAndAccessibleNamesBelowNormalPreview(int width, int height)
    {
        var view = Home(); Layout(view, width, height);
        Assert.AreEqual("1", Get<Button>(view, "DangoSlot1").Content);
        Assert.AreEqual("+", Get<Button>(view, "DangoSlot2").Content);
        Assert.AreEqual("3", Get<Button>(view, "DangoSlot3").Content);
        Assert.AreEqual(32d, Get<Button>(view, "DangoSlot2").Width);
        StringAssert.Contains(AutomationProperties.GetName(Get<Button>(view, "DangoSlot2")), "第 2 栏创建");
        Assert.IsTrue(Bounds(view, "DangoSlot1").Top > Bounds(view, "PetVisualArea").Bottom);
        Assert.IsTrue(Bounds(view, "DangoSlot3").Right <= Bounds(view, "Stage").Right + 1);
        var requests = new List<int>(); view.DangoSlotRequested += requests.Add;
        Get<Button>(view, "DangoSlot2").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CollectionAssert.AreEqual(new[] { 1 }, requests);
    }
    [STATestMethod]
    [DataRow(960, 540)]
    [DataRow(480, 800)]
    [DataRow(640, 360)]
    public void EditingBlocksAllSlotActionsAndPreservesTheirPositions(int width, int height)
    {
        var view = Home(); Layout(view, width, height);
        var original = Bounds(view, "PetPreview");
        view.ShowEditor(HomeEditor.Face); Layout(view, width, height);
        Assert.AreEqual(original.Top, Bounds(view, "PetPreview").Top, 1d);
        Assert.AreEqual(original.Height, Bounds(view, "PetPreview").Height, 1d);
        Assert.AreEqual(Visibility.Visible, Get<StackPanel>(view, "DangoSlotPanel").Visibility);
        Assert.IsFalse(Get<Button>(view, "DangoSlot1").IsEnabled);
        var overlap = Rect.Intersect(Bounds(view, "DangoSlotPanel"), Bounds(view, "FaceEditorSaveActions"));
        Assert.IsTrue(overlap.IsEmpty || overlap.Height <= 1 || overlap.Width <= 1, $"Slot controls overlap save actions: {overlap}, stage={Bounds(view, "Stage")}, pet={Bounds(view, "PetVisualArea")}, slots={Bounds(view, "DangoSlotPanel")}, saves={Bounds(view, "FaceEditorSaveActions")}");
        var requests = 0; view.DangoSlotRequested += _ => requests++;
        Get<Button>(view, "DangoSlot2").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(0, requests);
    }
    [STATestMethod]
    [DataRow(false, false, 1, 0)]
    [DataRow(true, false, 2, 0)]
    [DataRow(true, true, 2, 1)]
    public void PermanentDeleteRequiresBothTargetSpecificConfirmations(bool first, bool second, int expectedPrompts, int expectedDeletes)
    {
        var view = Home(); var prompts = 0; var deletes = 0;
        view.DangoDeleteConfirmation = (_, text) => { StringAssert.Contains(text, "第 1 栏"); StringAssert.Contains(text, "米团"); return ++prompts == 1 ? first : second; };
        view.DangoDeleteRequested += id => { Assert.AreEqual(First, id); deletes++; };
        view.RequestDangoDeletion(First);
        Assert.AreEqual(expectedPrompts, prompts); Assert.AreEqual(expectedDeletes, deletes);
    }
    [STATestMethod]
    public void DeleteRevalidatesCapturedIdentityAfterFirstConfirmation()
    {
        var view = Home(); var deletes = 0; var prompts = 0;
        view.DangoDeleteRequested += _ => deletes++;
        view.DangoDeleteConfirmation = (_, _) => { prompts++; view.ConfigureDangoSlots([new(1, Guid.NewGuid(), "另一只", true), new(2, null, null, false), new(3, Third, "豆团", false)], First, "米团", false); return true; };
        view.RequestDangoDeletion(First);
        Assert.AreEqual(1, prompts); Assert.AreEqual(0, deletes);
    }
    [STATestMethod]
    public void AccountPreferenceUnavailableAndNoSelectionNeverDrawsDefaultBody()
    {
        var view = Home(); view.SetOrdinaryRoomNamePreference(false, false);
        Assert.IsFalse(Get<RadioButton>(view, "OrdinaryRoomAccountName").IsEnabled);
        view.ConfigureDangoSlots([new(1, null, null, false), new(2, null, null, false), new(3, null, null, false)], null, null, false);
        Assert.AreEqual(Visibility.Hidden, Get<FrameworkElement>(view, "LiveEntityLayer").Visibility);
        Assert.AreEqual(Visibility.Visible, Get<StackPanel>(view, "DangoSlotPanel").Visibility);
    }
    [STATestMethod]
    public void CreateRequiresNameAndExactBodyConfirmationThenCancelRestoresSessionWithoutOccupancy()
    {
        var view = Home();
        var path = Path.Combine(Path.GetTempPath(), "dango-slot-test-" + Guid.NewGuid().ToString("N"));
        var saved = new AppearanceSettingsSession(Path.Combine(path, "saved.json"));
        var accessories = new AccessorySettingsSession(path, "none");
        view.ReplaceDangoSessions(saved, accessories);
        var draft = new AppearanceSettingsSession(Path.Combine(path, "draft.json"));
        view.BeginDangoCreation(1, draft, accessories);
        Assert.IsFalse(Get<Button>(view, "ApplyButton").IsEnabled);
        Get<TextBox>(view, "DangoCreationName").Text = "新团子";
        Assert.IsFalse(Get<Button>(view, "ApplyButton").IsEnabled, "A name alone must not bypass actual body display.");
        draft.MarkBodyApplied(draft.DraftAppearance.Body, draft.DraftAppearance.BodyPhysicsModel, draft.BodyConfigurationRevision, draft.BodyEpoch);
        Get<TextBox>(view, "DangoCreationName").Text = "";
        Assert.IsFalse(Get<Button>(view, "ApplyButton").IsEnabled);
        Get<TextBox>(view, "DangoCreationName").Text = "新团子";
        Assert.IsTrue(Get<Button>(view, "ApplyButton").IsEnabled);
        var cancelled = 0; view.DangoCreationCancelled += () => cancelled++;
        view.CloseEditor();
        Assert.AreEqual(1, cancelled); Assert.AreSame(saved, view.BoundAppearance);
        Assert.AreEqual("+", Get<Button>(view, "DangoSlot2").Content);
        Assert.IsFalse(view.IsDangoCreating);
        Assert.IsFalse(Directory.Exists(path), "Canceled creation must not persist anything.");
    }
    [TestMethod]
    public void NamesCountUnicodeTextElementsAndRejectInvalidInput()
    {
        Assert.IsTrue(ProductHomeView.TryNormalizeDangoName(string.Concat(Enumerable.Repeat("🍡", 24)), out _));
        Assert.IsFalse(ProductHomeView.TryNormalizeDangoName(string.Concat(Enumerable.Repeat("🍡", 25)), out _));
        Assert.IsFalse(ProductHomeView.TryNormalizeDangoName("团\n子", out _));
        Assert.IsFalse(ProductHomeView.TryNormalizeDangoName("\ud800", out _));
        Assert.IsTrue(ProductHomeView.TryNormalizeDangoName("  米团  ", out var name));
        Assert.AreEqual("米团", name);
    }
    [STATestMethod]
    public void UnknownCreateWarningKeepsDraftBlockedUntilHostReconciliation()
    {
        var view = Home(); var path = Path.Combine(Path.GetTempPath(), "dango-unknown-test-" + Guid.NewGuid().ToString("N"));
        var draft = new AppearanceSettingsSession(Path.Combine(path, "draft.json"));
        view.BeginDangoCreation(1, draft, new AccessorySettingsSession(path, "none"));
        view.ConfigureDangoSlots(Slots(), First, "米团", true, "创建结果待核对");
        view.CloseEditor();
        Assert.IsTrue(view.IsDangoCreating); Assert.IsTrue(view.IsEditing);
        Assert.AreEqual(Visibility.Visible, Get<Button>(view, "DangoReloadButton").Visibility);
        Assert.IsFalse(Get<Button>(view, "ApplyButton").IsEnabled);
    }
}
