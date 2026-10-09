using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Persistence;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    private DesktopAccountController? accountController;
    private DesktopRoomController? roomController;

    private void BindAccounts(ControlCenterWindow window)
    {
        var home = window.ProductHome;
        var paths = new DangoUserDataPaths(settingsPath);
        var repository = new LocalPetConfigurationRepository(paths.AppearancePath, appearance, home.Dispatcher);
        var configured = Environment.GetEnvironmentVariable("DANGO_SERVER_ORIGIN");
        Uri? origin = null;
        if (Uri.TryCreate(configured, UriKind.Absolute, out var candidate) && candidate.Scheme == "https"
            && candidate.UserInfo.Length == 0 && candidate.AbsolutePath == "/"
            && candidate.Query.Length == 0 && candidate.Fragment.Length == 0)
            origin = candidate;
        accountController = new(home.AccountPanel, origin, new WindowsAccountSecretStore(),
            CreatePetConfigurationCoordinator, repository.CaptureAsync, home.HideAccountPanel,
            (signedIn, ready) =>
            {
                if (!signedIn) roomController?.Reset();
                home.SetAccountStatus(signedIn, ready); home.RefreshAccountAppearance();
            });
        roomController = new(home.RoomPanel, () => accountController.RoomClient, home.HideAccountPanel,
            home.ShowAccountPanel, rejected => { accountController.RejectRoomAuthentication(rejected); home.ShowAccountPanel(); });
        void OpenRooms()
        {
            if (!accountController.IsLoggedIn) { home.ShowAccountPanel(); return; }
            home.ShowRoomPanel(); roomController.Open();
        }
        home.RoomDirectoryRequested += OpenRooms;
        accountController.RoomsRequested += OpenRooms;
        home.RoomPanelClosed += roomController.Close;
        home.AccountRequested += accountController.Open;
        home.AccountPanelClosed += accountController.Close;
    }
}
