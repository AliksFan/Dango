namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    internal void RefreshAccountAccessories()
    {
        Dispatcher.VerifyAccess();
        if (accessorySession is null) return;
        accessoryPanel.DiscardInputsAndRefresh();
        BoundRuntime?.SetAccessories(accessorySession.SavedConfiguration, !accessorySession.IsCompatibilityMode);
        RefreshAccessoryFrame();
    }
}
