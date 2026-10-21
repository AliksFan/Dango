using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

internal static class ProductHostTestSupport
{
    // Wait for the actual worker acknowledgement and catalog, not a fixed UI delay.
    // This helper never opens or activates a window.
    internal static void WaitForHome(DangoApplicationHost host)
    {
        var window = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetField("controlCenterWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var home = window.ProductHome;
        var clock = Stopwatch.StartNew();
        while ((home.DisplayedBeltSnapshot is not { Error: null, Frame: not null } ||
                home.BoundAppearance?.RequiresBodyPreview != false || home.CatalogSnapshot is null) && clock.ElapsedMilliseconds < 10000)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        Assert.IsNotNull(home.DisplayedBeltSnapshot?.Frame, home.ReadBeltBodySettingsScene().Diagnostics);
        Assert.IsNull(home.DisplayedBeltSnapshot.Error);
        Assert.IsFalse(home.BoundAppearance!.RequiresBodyPreview);
        Assert.IsNotNull(home.CatalogSnapshot, "The real catalog must finish before requesting a room.");
        Assert.IsTrue(home.CatalogSnapshot.ActiveCount > 0, "The real catalog must provide a saved active room before entry.");
    }
    internal static void WaitFor(Func<bool> condition, string reason)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 10000)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        Assert.IsTrue(condition(), reason);
    }
}
