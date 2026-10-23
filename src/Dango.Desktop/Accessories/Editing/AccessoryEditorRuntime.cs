using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Dango.Desktop.Accessories.Editing;

internal enum AccessoryEditorRuntimeState { Idle, Starting, Ready, Failed, Closed }

/// <summary>
/// Local-only browser lifecycle, owned by the editor UI thread. It never owns an accessory timer,
/// source file picker or persistent draft. Failure leaves the native caller's snapshot untouched.
/// </summary>
internal sealed class AccessoryEditorRuntime : IDisposable
{
    internal static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(20);
    // The all-source request filter covers documents, shared workers and service workers (Runtime 126+).
    internal const string MinimumRuntimeVersion = "126.0.2592.68";
    private readonly Func<string> getRuntimeVersion;
    private readonly Dispatcher owner = Dispatcher.CurrentDispatcher;
    private CoreWebView2Environment? environment;
    private CoreWebView2Controller? controller;
    private IReadOnlySet<string> resources = new HashSet<string>();
    private TaskCompletionSource<bool>? navigation;
    private int generation;
    private bool disposed;
    internal AccessoryEditorRuntimeState State { get; private set; }
    internal string? ErrorMessage { get; private set; }
    internal string? RuntimeVersion { get; private set; }
    internal event Action? StateChanged;
    internal event Action<string, string>? MessageReceived;
    internal CoreWebView2? Core => controller?.CoreWebView2;
    internal IReadOnlyList<int> GetProcessIds() => environment?.GetProcessInfos().Select(info => info.ProcessId).ToArray() ?? [];

    internal AccessoryEditorRuntime(Func<string>? getRuntimeVersion = null) =>
        this.getRuntimeVersion = getRuntimeVersion ?? (() => CoreWebView2Environment.GetAvailableBrowserVersionString());

    internal async Task<bool> InitializeAsync(nint parentWindow, string assetDirectory, string userDataDirectory)
    {
        owner.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        var currentGeneration = ++generation;
        ReleaseBrowser();
        SetState(AccessoryEditorRuntimeState.Starting);
        var initialization = InitializeCoreAsync(currentGeneration, parentWindow, assetDirectory, userDataDirectory);
        try { return await initialization.WaitAsync(InitializationTimeout); }
        catch (TimeoutException)
        {
            if (currentGeneration == generation)
            {
                ++generation; ReleaseBrowser();
                Fail("编辑器启动超时。草稿仍保留，可重试或取消。");
            }
            return false;
        }
    }

    private async Task<bool> InitializeCoreAsync(int currentGeneration, nint parentWindow, string assets, string userData)
    {
        CoreWebView2Controller? createdController = null;
        try
        {
            RuntimeVersion = getRuntimeVersion();
            if (string.IsNullOrEmpty(RuntimeVersion) ||
                CoreWebView2Environment.CompareBrowserVersions(RuntimeVersion, MinimumRuntimeVersion) < 0)
                throw new WebView2RuntimeNotFoundException();
            resources = AccessoryEditorAssets.Verify(assets);
            var createdEnvironment = await CoreWebView2Environment.CreateAsync(null, userData,
                new CoreWebView2EnvironmentOptions { AllowSingleSignOnUsingOSPrimaryAccount = false });
            if (disposed || currentGeneration != generation) return false;
            var options = createdEnvironment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            createdController = await createdEnvironment.CreateCoreWebView2ControllerAsync(parentWindow, options);
            if (disposed || currentGeneration != generation) { CloseController(createdController); return false; }
            environment = createdEnvironment; controller = createdController;
            controller.IsVisible = false;
            Configure(Path.GetFullPath(assets));
            navigation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Core!.Navigate(AccessoryEditorBridge.EntryPoint);
            var loaded = await navigation.Task;
            if (disposed || currentGeneration != generation) return false;
            if (!loaded) { ReleaseBrowser(); Fail("本地编辑器加载失败。草稿仍保留，可重试或取消。"); return false; }
            var usable = await Core.ExecuteScriptAsync("Boolean(window.DangoMiniPaint?.apiVersion===1 && typeof window.DangoMiniPaint.load==='function' && typeof window.DangoMiniPaint.exportProject==='function' && typeof window.DangoMiniPaint.exportPng==='function')");
            if (disposed || currentGeneration != generation) return false;
            if (usable != "true") throw new InvalidDataException("Local editor did not become usable.");
            SetState(AccessoryEditorRuntimeState.Ready);
            return true;
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException
            or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException
            or COMException or DllNotFoundException)
        {
            if (!ReferenceEquals(createdController, controller)) CloseController(createdController);
            if (disposed || currentGeneration != generation) return false;
            ReleaseBrowser();
            Fail(error is WebView2RuntimeNotFoundException ?
                "需要 Microsoft Edge WebView2 Runtime。请安装或更新后重试；草稿仍保留，也可取消。" :
                "编辑器无法启动。请检查本地资源或 Runtime 后重试；草稿仍保留，也可取消。");
            return false;
        }
    }

    private void Configure(string directory)
    {
        var core = Core!;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.SetVirtualHostNameToFolderMapping("editor.dango.invalid", directory, CoreWebView2HostResourceAccessKind.DenyCors);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.WebResourceRequested += OnWebResourceRequested;
        core.NewWindowRequested += OnNewWindow;
        core.PermissionRequested += OnPermission;
        core.DownloadStarting += OnDownload;
        core.ProcessFailed += OnProcessFailed;
        core.WebMessageReceived += OnMessage;
        environment!.BrowserProcessExited += OnBrowserProcessExited;
    }

    internal void SetBounds(System.Drawing.Rectangle bounds, bool visible)
    {
        owner.VerifyAccess();
        if (controller is null) return;
        controller.Bounds = bounds;
        controller.IsVisible = visible && State == AccessoryEditorRuntimeState.Ready;
    }

    internal bool Post(string json)
    {
        owner.VerifyAccess();
        if (State != AccessoryEditorRuntimeState.Ready || Core is null ||
            System.Text.Encoding.UTF8.GetByteCount(json) > AccessoryEditorBridge.MaximumMessageBytes) return false;
        Core.PostWebMessageAsJson(json); return true;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args) =>
        args.Cancel = args.Uri != AccessoryEditorBridge.EntryPoint;
    private void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args) => args.Cancel = true;
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) => navigation?.TrySetResult(args.IsSuccess);
    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs args) => args.Handled = true;
    private void OnPermission(object? sender, CoreWebView2PermissionRequestedEventArgs args) => args.State = CoreWebView2PermissionState.Deny;
    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs args) => args.Cancel = true;
    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (State != AccessoryEditorRuntimeState.Ready || args.Source != AccessoryEditorBridge.EntryPoint) return;
        var json = args.WebMessageAsJson;
        if (json.Length <= AccessoryEditorBridge.MaximumMessageBytes &&
            System.Text.Encoding.UTF8.GetByteCount(json) <= AccessoryEditorBridge.MaximumMessageBytes)
            MessageReceived?.Invoke(args.Source, json);
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (args.Request.Method == "GET" && IsAllowedResource(args.Request.Uri, args.ResourceContext, resources)) return;
        args.Response = environment!.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
    }

    internal static bool IsAllowedResource(string value, CoreWebView2WebResourceContext context, IReadOnlySet<string> resources)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (context == CoreWebView2WebResourceContext.Image &&
            (value.StartsWith("data:image/png;base64,", StringComparison.Ordinal) ||
             value.StartsWith("blob:" + AccessoryEditorBridge.Origin + "/", StringComparison.Ordinal))) return true;
        return uri.Scheme == "https" && uri.Host == "editor.dango.invalid" && uri.IsDefaultPort &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            resources.Contains(uri.AbsolutePath.TrimStart('/'));
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args) =>
        Fail("编辑器进程已退出或无响应。草稿仍保留，可重试或取消。");
    private void OnBrowserProcessExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs args)
    {
        if (!disposed && State != AccessoryEditorRuntimeState.Closed)
            Fail("编辑器进程已退出。草稿仍保留，可重试或取消。");
    }
    private void Fail(string message)
    {
        navigation?.TrySetResult(false);
        ErrorMessage = message;
        SetState(AccessoryEditorRuntimeState.Failed);
    }
    private void SetState(AccessoryEditorRuntimeState state)
    {
        State = state;
        if (state != AccessoryEditorRuntimeState.Failed) ErrorMessage = null;
        StateChanged?.Invoke();
    }
    private void ReleaseBrowser()
    {
        navigation?.TrySetResult(false); navigation = null;
        if (Core is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting; core.FrameNavigationStarting -= OnFrameNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted; core.WebResourceRequested -= OnWebResourceRequested;
            core.NewWindowRequested -= OnNewWindow; core.PermissionRequested -= OnPermission;
            core.DownloadStarting -= OnDownload; core.ProcessFailed -= OnProcessFailed; core.WebMessageReceived -= OnMessage;
        }
        if (environment is not null) environment.BrowserProcessExited -= OnBrowserProcessExited;
        CloseController(controller); controller = null; environment = null;
    }
    private static void CloseController(CoreWebView2Controller? target)
    {
        try { target?.Close(); }
        catch (Exception error) when (error is COMException or InvalidOperationException) { }
    }
    public void Dispose()
    {
        owner.VerifyAccess();
        if (disposed) return;
        disposed = true; ++generation;
        ReleaseBrowser(); SetState(AccessoryEditorRuntimeState.Closed);
    }
}
