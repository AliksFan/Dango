using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class RoomDangoNameTests
{
    [STATestMethod]
    public void ActualBadgeShowsOnlyHealthyOrdinaryRoomAndClearsAcrossPresentationChanges()
    {
        var scene = new LocalRoomSceneView { Width = 900, Height = 560 };
        scene.Measure(new(900, 560)); scene.Arrange(new(0, 0, 900, 560));
        scene.SetDangoName("虚构团子甲");
        Assert.IsNull(scene.RenderedDangoName);
        SetPresentationBounds(scene);
        scene.SetDangoName("虚构团子甲");
        Assert.AreEqual("虚构团子甲", scene.RenderedDangoName);
        var badge = Descendants<Border>(scene).Single(b => b.Child is TextBlock { Text: "虚构团子甲" });
        Assert.IsFalse(badge.IsHitTestVisible);
        Assert.IsTrue(Canvas.GetTop(badge) + badge.DesiredSize.Height <= scene.PetScreenBounds!.Value.Y - 8);
        var before = Canvas.GetLeft(badge);
        scene.SelectView(RoomViewPreset.Near);
        Assert.AreNotEqual(before, Canvas.GetLeft(badge), "The actual label must follow the reprojected body.");
        Assert.IsTrue(Canvas.GetTop(badge) + badge.DesiredSize.Height <= scene.PetScreenBounds!.Value.Y - 8);
        scene.SetDangoName("虚构账号乙");
        Assert.AreEqual("虚构账号乙", scene.RenderedDangoName);
        scene.BeginPresentation(); Assert.IsNull(scene.RenderedDangoName);
        SetPresentationBounds(scene); scene.SetDangoName("虚构团子甲");
        scene.ShowUnavailable("虚构故障"); Assert.IsNull(scene.RenderedDangoName);
        SetPresentationBounds(scene); scene.SetDangoName("虚构团子甲");
        scene.EnableDesktopPetPresentation(); Assert.IsNull(scene.RenderedDangoName);
        scene.SetDangoName("虚构账号乙"); Assert.IsNull(scene.RenderedDangoName);
    }
    private static void SetPresentationBounds(LocalRoomSceneView scene)
    {
        // Isolate the visual contract without a native owner, HWND, timer, or input injection.
        typeof(LocalRoomSceneView).GetProperty("HasHealthyFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(scene, true);
        typeof(LocalRoomSceneView).GetField("visibleWorldBounds", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(scene,
            (LogicalRect?)new LogicalRect(1000, 200, 160, 120));
        typeof(LocalRoomSceneView).GetMethod("UpdatePetScreenBounds", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(scene, null);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
