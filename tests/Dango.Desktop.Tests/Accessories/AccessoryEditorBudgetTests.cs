using System.Diagnostics;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorBudgetTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod, TestCategory("AccessoryEditorBudget")]
    public void MaximumWorkingCanvasAndLayerPixelBudgetHaveMeasuredOfflineBounds()
    {
        var old = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            using var window = new HwndSource(new HwndSourceParameters("Dango synthetic editor budget")
            { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
            using var runtime = new AccessoryEditorRuntime();
            var timer = Stopwatch.StartNew();
            Assert.IsTrue(Pump(runtime.InitializeAsync(window.Handle, Path.Combine(AppContext.BaseDirectory, "AccessoryEditor"),
                Path.Combine(TestRepository.Root, "artifacts", "accessory-editor-budget", Guid.NewGuid().ToString("N")))), runtime.ErrorMessage);
            var initializationMs = timer.Elapsed.TotalMilliseconds;
            var peakBytes = PrivateBytes(runtime);
            const string script = """
                window.dangoBudget={done:false};
                (async()=>{try{
                  const c=document.createElement('canvas');c.width=4000;c.height=4000;
                  const x=c.getContext('2d');x.fillStyle='#885e3e';x.fillRect(100,100,3800,3800);
                  const t=performance.now(), b=c.toDataURL('image/png').split(',')[1];
                  await DangoMiniPaint.load({width:4000,height:4000,pngBase64:b});
                  const first=DangoMiniPaint.exportProject();
                  const images=Array.from({length:4},(_,i)=>({...first.layers[0],id:i+1,assetId:'asset-'+(i+1),order:i+1}));
                  const assets=Array.from({length:4},(_,i)=>({id:'asset-'+(i+1),pngBase64:b}));
                  await DangoMiniPaint.load({...first,layers:images,assets});
                  const p=DangoMiniPaint.exportProject(),png=DangoMiniPaint.exportPng();
                  window.dangoBudget={done:true,ms:performance.now()-t,layers:p.layers.length,
                    projectBytes:new TextEncoder().encode(JSON.stringify(p)).length,pngChars:png.pngBase64.length};
                }catch(e){window.dangoBudget={done:true,error:e.name+': '+e.message}}})();
                """;
            Pump(runtime.Core!.ExecuteScriptAsync(script));
            timer.Restart(); JsonElement result = default;
            while (timer.Elapsed < TimeSpan.FromSeconds(60))
            {
                peakBytes = Math.Max(peakBytes, PrivateBytes(runtime));
                var json = Pump(runtime.Core.ExecuteScriptAsync("JSON.stringify(window.dangoBudget)"));
                using var document = JsonDocument.Parse(JsonSerializer.Deserialize<string>(json)!);
                result = document.RootElement.Clone();
                if (result.GetProperty("done").GetBoolean()) break;
                Pump(Task.Delay(25));
            }
            TestContext.WriteLine($"initializationMs={initializationMs:F1}; peakBrowserPrivateBytes={peakBytes}; result={result}");
            Assert.IsTrue(result.GetProperty("done").GetBoolean(), "Maximum working image operation timed out.");
            Assert.IsFalse(result.TryGetProperty("error", out _), result.ToString());
            Assert.AreEqual(4, result.GetProperty("layers").GetInt32());
            Assert.IsTrue(peakBytes < 1536L * 1024 * 1024, "The selected browser working-set budget must stay below 1.5 GiB on the measured device.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }

    private static long PrivateBytes(AccessoryEditorRuntime runtime)
    {
        long total = 0;
        foreach (var id in runtime.GetProcessIds())
            try { using var process = Process.GetProcessById(id); total += process.PrivateMemorySize64; }
            catch (ArgumentException) { }
        return total;
    }
    private static T Pump<T>(Task<T> task) { Pump((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher; var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
