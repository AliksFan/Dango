using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorBackgroundTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    public void HiddenNativeControllerLoadsThePackagedOfflineEditorAndReopensItsPixels()
    {
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { VerifyHiddenController(); }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    private void VerifyHiddenController()
    {
        // No WS_VISIBLE, ShowWindow, focus, activation or global input. This verifies scripting only.
        using var window = new HwndSource(new HwndSourceParameters("Dango synthetic editor check")
        { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
        Assert.IsFalse(IsWindowVisible(window.Handle));
        var userData = Path.Combine(TestRepository.Root, "artifacts", "accessory-editor-background", Guid.NewGuid().ToString("N"));
        using var runtime = new AccessoryEditorRuntime();
        var initialized = Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), userData));
        Assert.IsTrue(initialized, runtime.ErrorMessage);
        Assert.AreEqual(AccessoryEditorRuntimeState.Ready, runtime.State);
        TestContext.WriteLine("Runtime: " + runtime.RuntimeVersion);
        var core = runtime.Core!;
        var capabilities = Pump(core.ExecuteScriptAsync("JSON.stringify({load:typeof window.DangoMiniPaint?.load,project:typeof window.DangoMiniPaint?.exportProject,png:typeof window.DangoMiniPaint?.exportPng})"));
        StringAssert.Contains(capabilities, "function");
        const string check = """
            window.dangoCheck = {done:false};
            (async () => {
              try {
                const c=document.createElement('canvas');c.width=12;c.height=8;
                const x=c.getContext('2d');x.fillStyle='#885e3e';x.fillRect(2,2,6,4);
                await DangoMiniPaint.load({width:12,height:8,pngBase64:c.toDataURL('image/png').split(',')[1]});
                const png=DangoMiniPaint.exportPng().pngBase64, project=DangoMiniPaint.exportProject();
                await DangoMiniPaint.load(project);
                window.dangoCheck={done:true,equal:png===DangoMiniPaint.exportPng().pngBase64,layers:project.layers.length,
                  safe:!JSON.stringify(project).includes('https:')&&!JSON.stringify(project).includes('file:'),
                  width:project.width,height:project.height};
              } catch(e) { window.dangoCheck={done:true,error:e.name+': '+e.message}; }
            })();
            """;
        Pump(core.ExecuteScriptAsync(check));
        string result = "";
        for (var i = 0; i < 100; i++)
        {
            result = Pump(core.ExecuteScriptAsync("JSON.stringify(window.dangoCheck)"));
            var decoded = JsonSerializer.Deserialize<string>(result)!;
            using var status = JsonDocument.Parse(decoded);
            if (status.RootElement.GetProperty("done").GetBoolean()) break;
            Pump(Task.Delay(50));
        }
        TestContext.WriteLine(result);
        using var final = JsonDocument.Parse(JsonSerializer.Deserialize<string>(result)!);
        var value = final.RootElement;
        Assert.IsFalse(value.TryGetProperty("error", out _), result);
        Assert.IsTrue(value.GetProperty("done").GetBoolean(), result);
        Assert.IsTrue(value.GetProperty("equal").GetBoolean(), result);
        Assert.IsTrue(value.GetProperty("safe").GetBoolean(), result);
        Assert.AreEqual(12, value.GetProperty("width").GetInt32());
        Assert.AreEqual(8, value.GetProperty("height").GetInt32());
        Assert.IsFalse(IsWindowVisible(window.Handle));
        Pump(core.ExecuteScriptAsync("location.href='https://blocked-editor-check.invalid/';"));
        Pump(Task.Delay(100));
        Assert.AreEqual(AccessoryEditorBridge.EntryPoint, core.Source, "External navigation must be cancelled before issuing a request.");
        // Crash only this isolated page's renderer; do not kill a shared or user-owned process.
        var crash = core.CallDevToolsProtocolMethodAsync("Page.crash", "{}");
        try { Pump(crash.WaitAsync(TimeSpan.FromSeconds(3))); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException) { }
        for (var i = 0; i < 50 && runtime.State != AccessoryEditorRuntimeState.Failed; i++) Pump(Task.Delay(50));
        Assert.AreEqual(AccessoryEditorRuntimeState.Failed, runtime.State);
        StringAssert.Contains(runtime.ErrorMessage, "草稿仍保留");
        Assert.IsTrue(Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"), userData)), runtime.ErrorMessage);
        Assert.IsFalse(IsWindowVisible(window.Handle));
        runtime.Dispose();
        Assert.AreEqual(AccessoryEditorRuntimeState.Closed, runtime.State);
    }

    private static T Pump<T>(Task<T> task)
    {
        Pump((Task)task); return task.GetAwaiter().GetResult();
    }
    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
