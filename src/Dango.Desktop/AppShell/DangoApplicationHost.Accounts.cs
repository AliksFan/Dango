using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Persistence;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    private DesktopAccountController? accountController;
    private DesktopRoomController? roomController;
    private DesktopOnlineRoomController? onlineRoomController;

    private void BindAccounts(ControlCenterWindow window)
    {
        var home = window.ProductHome;
        var paths = new DangoUserDataPaths(settingsPath);
        var repository = new LocalPetConfigurationRepository(paths.AppearancePath, appearance, home.Dispatcher);
        var dependencies = accountDependencies;
        Uri? origin = dependencies?.Origin;
        if (dependencies is null)
        {
            var configured = Environment.GetEnvironmentVariable("DANGO_SERVER_ORIGIN");
            if (Uri.TryCreate(configured, UriKind.Absolute, out var candidate) && candidate.Scheme == "https"
                && candidate.UserInfo.Length == 0 && candidate.AbsolutePath == "/"
                && candidate.Query.Length == 0 && candidate.Fragment.Length == 0)
                origin = candidate;
        }
        accountController = new(home.AccountPanel, origin, dependencies?.SecretStore ?? new WindowsAccountSecretStore(),
            CreatePetConfigurationCoordinator, repository.CaptureAsync, home.HideAccountPanel,
            (signedIn, ready) =>
            {
                if (!signedIn) roomController?.Reset();
                onlineRoomController?.SynchronizeBinding();
                home.SetAccountStatus(signedIn, ready, accountController?.Nickname); home.RefreshAccountAppearance();
            }, createClient: dependencies?.CreateClient, createSync: CreatePetConfigurationLiveSync);
        appearance.SavedToDisk += OnAccountAppearanceSaved;
        roomController = new(home.RoomPanel, () => accountController.RoomClient, home.HideAccountPanel,
            home.ShowAccountPanel, rejected => { accountController.RejectRoomAuthentication(rejected); home.ShowAccountPanel(); });
        void OpenRooms()
        {
            if (!accountController.IsLoggedIn) { home.ShowAccountPanel(); return; }
            home.ShowRoomPanel(); roomController.Open();
        }
        onlineRoomController = new(home.OnlineRoomPanel, () => accountController.RoomAccessClient,
            () => accountController.ConfigurationReady, home.HideAccountPanel, OpenRooms, home.ShowAccountPanel,
            rejected => { accountController.RejectRoomAuthentication(rejected); home.ShowAccountPanel(); });
        roomController.OnlineRequested += target =>
        {
            if (!accountController.IsLoggedIn) { home.ShowAccountPanel(); return; }
            home.ShowOnlineRoomPanel(); onlineRoomController.Open(target);
        };
        home.RoomDirectoryRequested += roomController.Open;
        home.RoomPanelClosed += roomController.Close;
        home.OnlineRoomPanelClosed += onlineRoomController.Close;
        home.AccountRequested += accountController.Open;
        home.AccountPanelClosed += accountController.Close;
        accountController.AccountAttentionRequested += () => { home.ShowAccountPanel(); accountController.Open(); };
        home.EnablePorchAccountInteractions();
        accountController.InitializeRememberedSession();
    }

    private void OnAccountAppearanceSaved() => accountController?.NotifyAppearanceSaved();
}
