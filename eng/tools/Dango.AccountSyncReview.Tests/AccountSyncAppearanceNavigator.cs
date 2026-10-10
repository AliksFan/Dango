namespace Dango.AccountSyncReview.Tests;

internal enum AccountSyncAppearanceEditor { None, Face, Wardrobe }

internal sealed record AccountSyncAppearanceNavigationState(
    bool AccountOpen, bool SettingsOpen, bool InPorch, bool DangoAway,
    AccountSyncAppearanceEditor Editor, bool FaceEntryReady, bool EyesTabReady,
    bool EyesPanelReady, bool ApplyReady, bool CancelReady, bool CloseReady,
    bool PendingSave, double DraftEyeSize, double SavedEyeSize);

// Shared source, without a test-project reference or runtime resources. The owned-window
// caller supplies actual UIA operations and a dispatcher-pumping bounded wait.
internal sealed class AccountSyncAppearanceNavigator
{
    private readonly Action<string> invokeId;
    private readonly Action<string, double> range;
    private readonly Action<Func<bool>> wait;
    private readonly Func<AccountSyncAppearanceNavigationState> read;
    private readonly Action<string, AccountSyncAppearanceNavigationState>? trace;

    internal AccountSyncAppearanceNavigator(Action<string> invokeId, Action<string, double> range,
        Action<Func<bool>> wait, Func<AccountSyncAppearanceNavigationState> read,
        Action<string, AccountSyncAppearanceNavigationState>? trace = null)
    {
        this.invokeId = invokeId ?? throw new ArgumentNullException(nameof(invokeId));
        this.range = range ?? throw new ArgumentNullException(nameof(range));
        this.wait = wait ?? throw new ArgumentNullException(nameof(wait));
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.trace = trace;
    }

    internal void PreviewEyeSize(double value)
    {
        if (!double.IsFinite(value) || value is < 70 or > 140) throw Reject();
        RequireHome();
        if (read().Editor == AccountSyncAppearanceEditor.Wardrobe) CloseEditor();
        if (read().Editor == AccountSyncAppearanceEditor.None)
        {
            Until("face-entry-ready", s => s.Editor == AccountSyncAppearanceEditor.None && s.FaceEntryReady);
            Invoke("FaceButton");
        }
        Until("face-editor-ready", s => s.Editor == AccountSyncAppearanceEditor.Face && s.EyesTabReady);
        Invoke("AppearanceEyesTab");
        Until("eyes-panel-ready", s => s.Editor == AccountSyncAppearanceEditor.Face && s.EyesPanelReady);
        Mark("before-range:EyeSizeSlider");
        range("EyeSizeSlider", value);
        Until("eye-preview-ready", s => s.Editor == AccountSyncAppearanceEditor.Face
            && s.EyesPanelReady && s.DraftEyeSize == value);
    }

    internal void ApplyEyeSize(double value)
    {
        RequireHome();
        Until("apply-ready", s => s.Editor == AccountSyncAppearanceEditor.Face
            && s.DraftEyeSize == value && s.PendingSave && s.ApplyReady);
        Invoke("ApplyButton");
        Until("eye-save-ready", s => s.Editor == AccountSyncAppearanceEditor.Face
            && !s.PendingSave && s.DraftEyeSize == value && s.SavedEyeSize == value);
    }

    internal void SaveEyeSize(double value)
    {
        PreviewEyeSize(value);
        // A request for the already saved value must not invoke a disabled save button.
        if (read().PendingSave) ApplyEyeSize(value);
        else if (read().SavedEyeSize != value) throw Reject();
        CloseEditor();
    }

    internal void CancelPreview()
    {
        RequireHome();
        Until("cancel-ready", s => s.Editor == AccountSyncAppearanceEditor.Face && s.CancelReady);
        Invoke("CancelAppearanceButton");
        Until("cancel-complete", s => s.Editor == AccountSyncAppearanceEditor.None
            && !s.PendingSave && s.DraftEyeSize == s.SavedEyeSize && s.FaceEntryReady);
    }

    internal void CloseEditor()
    {
        RequireHome();
        var editor = read().Editor;
        if (editor == AccountSyncAppearanceEditor.None) return;
        if (editor is not (AccountSyncAppearanceEditor.Face or AccountSyncAppearanceEditor.Wardrobe)) throw Reject();
        Until("close-ready", s => s.Editor == editor && s.CloseReady);
        Invoke(editor == AccountSyncAppearanceEditor.Face ? "FaceCloseEditorButton" : "CloseEditorButton");
        Until("close-complete", s => s.Editor == AccountSyncAppearanceEditor.None
            && !s.PendingSave && s.DraftEyeSize == s.SavedEyeSize && s.FaceEntryReady);
    }

    private void RequireHome()
    {
        var state = read();
        trace?.Invoke("require-home", state);
        if (!IsHome(state) || !Enum.IsDefined(state.Editor)) throw Reject();
    }

    private void Invoke(string id)
    {
        RequireHome();
        Mark("before-invoke:" + id);
        invokeId(id);
    }

    private void Until(string phase, Func<AccountSyncAppearanceNavigationState, bool> ready)
    {
        wait(() => { var state = read(); return IsHome(state) && ready(state); });
        Mark(phase);
    }

    private void Mark(string phase) => trace?.Invoke(phase, read());
    private static bool IsHome(AccountSyncAppearanceNavigationState state) =>
        !state.AccountOpen && !state.SettingsOpen && !state.InPorch && !state.DangoAway;
    private static InvalidOperationException Reject() => new("accountsync_appearance_navigation_unavailable");
}
