using System.Reflection;
using System.Windows;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ControlCenterWindow
{
    private Func<ProductSettingsSceneSnapshot>? readSettingsScene;
    private Action? restoreSettingsSceneDisplay;
    private Action? reinitializeSettingsScene;
    internal Func<bool> ConfirmSceneReinitialization { get; set; }

    internal void BindSettingsScene(Func<ProductSettingsSceneSnapshot> read, Action restoreDisplay, Action reinitialize)
    {
        readSettingsScene = read;
        restoreSettingsSceneDisplay = restoreDisplay;
        reinitializeSettingsScene = reinitialize;
        RefreshProductSettings();
    }

    private ProductSettingsSceneSnapshot ReadSettingsScene() => readSettingsScene?.Invoke() ?? new(
        "我的团子", commandController.InitializationState == Rendering.Visuals.SceneInitializationState.Ready ? "场景已就绪" : "团子尚未启动",
        commandController.GetSnapshot().LastError);

    private void RefreshProductSettings()
    {
        var scene = ReadSettingsScene();
        ProductHome.UpdateSettings(new(scene, settingsSession?.HasWarning == true,
            settingsSession is { IsWriteProtected: true } or { IsPresetWriteProtected: true }, settingsSession?.StatusMessage ?? string.Empty));
        if (ProductHome.IsSettingsDiagnosticsOpen) ProductHome.SetDiagnosticsText(CreateProductSettingsDiagnostics(scene));
    }

    private void RunSceneSettingsAction(bool reinitialize)
    {
        var scene = ReadSettingsScene();
        var requestedScene = scene;
        if (reinitialize ? !scene.CanReinitialize : !scene.CanRestoreDisplay) return;
        if (reinitialize && !ConfirmSceneReinitialization()) return;
        // The confirmation can pump messages; capabilities must still be valid afterwards.
        scene = ReadSettingsScene();
        if (scene.Name != requestedScene.Name || scene.Revision != requestedScene.Revision) { RefreshProductSettings(); return; }
        if (reinitialize ? !scene.CanReinitialize : !scene.CanRestoreDisplay) return;
        try
        {
            ProductHome.ShowSettingsStatus(string.Empty, failed: false);
            if (reinitialize) reinitializeSettingsScene?.Invoke();
            else restoreSettingsSceneDisplay?.Invoke();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            ProductHome.ShowSettingsStatus($"恢复未完成（{error.GetType().Name}），请查看当前场景状态后重试。", failed: true);
        }
        RefreshProductSettings();
    }

    private bool ConfirmSettingsSceneReinitialization() => System.Windows.MessageBox.Show(this,
        $"重新初始化“{ReadSettingsScene().Name}”中的团子将重置当前位置与运行状态。已保存的外观和家具会保留。是否继续？",
        "重新初始化团子", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private string CreateProductSettingsDiagnostics(ProductSettingsSceneSnapshot? snapshot = null)
    {
        var scene = snapshot ?? ReadSettingsScene();
        var version = typeof(ControlCenterWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ControlCenterWindow).Assembly.GetName().Version?.ToString() ?? "未知";
        return $"Dango {version}\n当前场景：{scene.Name}\n状态：{scene.Status}\n最近错误：{scene.Error ?? "无"}\n本机偏好：{settingsSession?.StatusMessage ?? "未连接保存会话"}\n{scene.Diagnostics}";
    }
}
