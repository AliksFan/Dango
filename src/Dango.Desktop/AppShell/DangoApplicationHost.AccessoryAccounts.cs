using Dango.Client.Sync.Accessories;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost
{
    private AccessoryAccountController? accessoryAccountController;

    private void BindAccessoryAccounts(ProductHomeView home)
    {
        var panel = new AccessoryAccountPanelView();
        home.AccountPanel.BindAccessoryAccountContent(panel);
        accessoryAccountController = new(panel, accessories, home.RefreshAccountAccessories,
            () => accountController?.InvalidateAuthenticatedSession());
        accountController!.AuthenticatedSessionChanged += OnAccessoryAccountAttachmentChanged;
    }

    private void OnAccessoryAccountAttachmentChanged(AuthenticatedAccountAttachment? attachment)
    {
        if (accessoryAccountController is null) return;
        if (attachment is null) { accessoryAccountController.Detach(); return; }
        var scope = new AccessoryAccountScope(attachment.Identity.UserId.ToString("N"),
            attachment.Identity.PetId.ToString("N"), attachment.Generation);
        accessoryAccountController.Attach(scope, attachment.Client.CreateAccessoryTransport(scope));
    }

    private void DisposeAccessoryAccounts()
    {
        if (accountController is not null)
            accountController.AuthenticatedSessionChanged -= OnAccessoryAccountAttachmentChanged;
        accessoryAccountController?.Dispose();
        accessoryAccountController = null;
    }
}
