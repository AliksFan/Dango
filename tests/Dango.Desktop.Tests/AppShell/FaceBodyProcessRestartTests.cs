using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Tests.AppShell;

/// <summary>Real app processes and UI Automation, restricted to owned fictional face-review data.</summary>
[TestClass]
[DoNotParallelize]
public sealed class FaceBodyProcessRestartTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("ProcessIntegration")]
    public void RealExecutableRequiresBodyPreviewBeforeSaveAndSecondProcessRestoresBody()
    {
        var runRoot = Path.GetFullPath(Path.Combine(TestRepository.Root, "artifacts", "face-body", "process",
            Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(runRoot);
        var reviewRoot = Path.Combine(runRoot, "fictional-review");
        Directory.CreateDirectory(reviewRoot);
        File.WriteAllText(Path.Combine(reviewRoot, ".dango-face-review.json"),
            "{\"application\":\"Dango\",\"faceReviewFormat\":1,\"fictional\":true}");
        var appearancePath = Path.Combine(reviewRoot, "appearance.json");
        var logPath = Path.Combine(runRoot, "process-events.jsonl");
        using var log = new VerificationLog(logPath);
        ReviewProcess? first = null;
        ReviewProcess? second = null;
        try
        {
            AssertRuntimeFiles();
            AssertReviewMutexIsFree();
            new ControlCenterWindowSettingsStore().Save(Path.Combine(reviewRoot, "settings.json"),
                ControlCenterWindowSettings.CreateDefault() with
                {
                    IntroSeen = true,
                    DisplayMode = Dango.Desktop.DesktopHost.DesktopDisplayMode.Window,
                });
            Assert.IsFalse(File.Exists(appearancePath), "The fixture must not pre-seed saved body controls.");

            first = ReviewProcess.Start(reviewRoot, "first", runRoot, log);
            var firstWindow = WaitForMainWindow(first, log);
            OpenBodyEditor(first, firstWindow, log);
            AssertBodyControls(first, firstWindow, DangoBodyControls.Default, log);

            var changed = new DangoBodyControls(120, 23, 81);
            foreach (var (name, value) in new[]
            {
                ("身体大小", changed.SizePercent),
                ("身体柔软度", changed.Softness),
                ("落地体型：胖到瘦", changed.LandingBuild),
            })
            {
                GetRange(WaitForControl(first, firstWindow, name, ControlType.Slider, log)).SetValue(value);
                WaitUntil(first, () => FindControl(firstWindow, name, ControlType.Slider) is { } slider &&
                    slider.Current.ProcessId == first.Process.Id &&
                    slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern) &&
                    Math.Abs(((RangeValuePattern)pattern).Current.Value - value) < 1e-9,
                    "staged body slider " + name, log);
                WaitUntil(first, () => FindControl(firstWindow, "应用并保存外观", ControlType.Button) is { Current.IsEnabled: false } &&
                    FindControl(firstWindow, "身体预览尚未更新", ControlType.Text) is not null,
                    "save blocked until exact body draft is previewed", log);
                Assert.IsFalse(File.Exists(appearancePath), "Changing a real UI slider must not write appearance.json.");
            }
            AssertBodyControls(first, firstWindow, changed, log);
            log.Write("first-process-draft-unsaved-and-save-disabled", new { first.Process.Id, changed });

            Invoke(WaitForControl(first, firstWindow, "预览身体并下落", ControlType.Button, log), log, "preview-real-body");
            WaitUntil(first, () => FindControl(firstWindow, "应用并保存外观", ControlType.Button) is { Current.IsEnabled: true } &&
                FindControl(firstWindow, "已预览 · 大小 120% · 柔软 23 · 体型 81", ControlType.Text) is not null,
                "successful real body preview enables saving", log);
            Assert.IsFalse(File.Exists(appearancePath), "A preview must still leave persistence untouched.");
            Invoke(WaitForControl(first, firstWindow, "应用并保存外观", ControlType.Button, log), log, "save-real-body");
            // Observe UI completion before opening the atomically replaced settings file.
            WaitUntil(first, () => FindControl(firstWindow, "外观已保存到本机。", ControlType.Text) is not null &&
                FindControl(firstWindow, "应用并保存外观", ControlType.Button) is { Current.IsEnabled: false },
                "actual UI save completion", log);
            AssertSavedBody(appearancePath, changed);
            var committedBytes = File.ReadAllBytes(appearancePath);
            log.Write("first-process-ui-save-confirmed-on-disk", new { first.Process.Id, changed });
            var firstPid = first.Process.Id;
            first.StopNormally(firstWindow);
            Assert.AreEqual(0, first.Process.ExitCode);
            first.Dispose(); first = null;

            second = ReviewProcess.Start(reviewRoot, "second", runRoot, log);
            Assert.AreNotEqual(firstPid, second.Process.Id, "Restoration must run in a different actual process.");
            var secondWindow = WaitForMainWindow(second, log);
            OpenBodyEditor(second, secondWindow, log);
            AssertBodyControls(second, secondWindow, changed, log);
            Assert.IsFalse(FindControl(secondWindow, "应用并保存外观", ControlType.Button)!.Current.IsEnabled,
                "Restored controls must be the saved state, not a new unsaved draft.");
            AssertSavedBody(appearancePath, changed);
            CollectionAssert.AreEqual(committedBytes, File.ReadAllBytes(appearancePath),
                "Opening the second process must not silently rewrite saved appearance.");
            log.Write("second-process-ui-and-disk-restored", new { firstPid, secondPid = second.Process.Id, changed });
            second.StopNormally(secondWindow);
            Assert.AreEqual(0, second.Process.ExitCode);
            log.Write("verification-passed", new { firstPid, secondPid = second.Process.Id, reviewRoot });
        }
        catch (Exception error)
        {
            log.Write("verification-failed", new { type = error.GetType().Name, error.Message });
            throw;
        }
        finally
        {
            second?.Dispose(); first?.Dispose();
            log.Write("retained-owned-verification-data", new { runRoot, reviewRoot });
            TestContext.WriteLine("Owned body process restart evidence: " + runRoot);
            TestContext.AddResultFile(logPath);
        }
    }

    private static void OpenBodyEditor(ReviewProcess child, AutomationElement window, VerificationLog log)
    {
        Invoke(WaitForControl(child, window, "D · 捏脸", ControlType.Button, log), log, "open-face-editor");
        Invoke(WaitForControl(child, window, "捏脸：身体", ControlType.Button, log), log, "open-body-tab");
    }

    private static void AssertBodyControls(ReviewProcess child, AutomationElement window, DangoBodyControls body, VerificationLog log)
    {
        Assert.AreEqual(body.SizePercent, GetRange(WaitForControl(child, window, "身体大小", ControlType.Slider, log)).Current.Value, 1e-9);
        Assert.AreEqual(body.Softness, GetRange(WaitForControl(child, window, "身体柔软度", ControlType.Slider, log)).Current.Value, 1e-9);
        Assert.AreEqual(body.LandingBuild, GetRange(WaitForControl(child, window, "落地体型：胖到瘦", ControlType.Slider, log)).Current.Value, 1e-9);
    }

    private static void AssertSavedBody(string path, DangoBodyControls body)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var saved = document.RootElement;
        Assert.AreEqual(4, saved.GetProperty("appearanceSchemaVersion").GetInt32());
        Assert.AreEqual(body.SizePercent, saved.GetProperty("bodySizePercent").GetDouble(), 1e-9);
        Assert.AreEqual(body.Softness, saved.GetProperty("bodySoftness").GetDouble(), 1e-9);
        Assert.AreEqual(body.LandingBuild, saved.GetProperty("bodyLandingBuild").GetDouble(), 1e-9);
        var loaded = new AppearanceSettingsStore().LoadOrDefault(path);
        Assert.IsNull(loaded.Warning);
        Assert.IsFalse(loaded.PreserveOriginal);
        Assert.IsFalse(loaded.RequiresMigration);
        Assert.AreEqual(body, loaded.Settings.Configuration.Body);
    }

    private static void AssertRuntimeFiles()
    {
        foreach (var name in new[] { "Dango.Desktop.exe", "Dango.Desktop.runtimeconfig.json", "dango_physics_module.dll", "dango_box2d.dll" })
            Assert.IsTrue(File.Exists(Path.Combine(AppContext.BaseDirectory, name)), "The real process acceptance requires output file " + name);
    }

    private static void AssertReviewMutexIsFree()
    {
        try
        {
            using var existing = Mutex.OpenExisting(App.ResolveSingleInstanceName(["--face-review"]) + ".Mutex");
            Assert.Fail("A face review instance is already active. This test must not activate or automate an unrelated review window.");
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    private static AutomationElement WaitForMainWindow(ReviewProcess child, VerificationLog log)
    {
        AutomationElement? window = null;
        WaitUntil(child, () =>
        {
            // Process.MainWindowHandle can identify the scene window during startup.
            // Only inspect the owned product control center.
            window = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, child.Process.Id),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.NameProperty, "Dango 控制中心")));
            return window is not null && window.Current.NativeWindowHandle != 0;
        }, "owned main window", log);
        child.Process.Refresh();
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, child.Process.Id));
        log.Write("owned-main-window-found", new
        {
            child.Process.Id,
            handle = window!.Current.NativeWindowHandle,
            processMainHandle = child.Process.MainWindowHandle.ToInt64(),
            ownedWindows = windows.Cast<AutomationElement>().Select(candidate => new
            { candidate.Current.Name, candidate.Current.NativeWindowHandle }).ToArray(),
        });
        return window!;
    }

    private static AutomationElement? FindControl(AutomationElement window, string name, ControlType type) =>
        window.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, type)));

    private static AutomationElement WaitForControl(ReviewProcess child, AutomationElement window, string name, ControlType type, VerificationLog log)
    {
        AutomationElement? result = null;
        WaitUntil(child, () => (result = FindControl(window, name, type)) is not null &&
            result.Current.ProcessId == child.Process.Id && result.Current.IsEnabled &&
            (type != ControlType.Slider || result.TryGetCurrentPattern(RangeValuePattern.Pattern, out _)) &&
            (type != ControlType.Button || result.TryGetCurrentPattern(InvokePattern.Pattern, out _)),
            "UIA control ready " + name, log);
        Assert.AreEqual(child.Process.Id, result!.Current.ProcessId, "UIA must stay within the app process created by this test.");
        return result;
    }

    private static RangeValuePattern GetRange(AutomationElement element)
    {
        Assert.IsTrue(element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern), "The real slider must expose RangeValuePattern.");
        return (RangeValuePattern)pattern;
    }

    private static void Invoke(AutomationElement element, VerificationLog log, string action)
    {
        Assert.IsTrue(element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern), "The real button must expose InvokePattern.");
        ((InvokePattern)pattern).Invoke();
        log.Write(action, new { element.Current.ProcessId, element.Current.Name });
    }

    private static void WaitUntil(ReviewProcess child, Func<bool> condition, string description, VerificationLog log)
    {
        var elapsed = Stopwatch.StartNew();
        string? lastError = null;
        while (elapsed.Elapsed < TimeSpan.FromSeconds(25))
        {
            child.Process.Refresh();
            Assert.IsFalse(child.Process.HasExited, $"The owned app process exited before {description}; see retained process logs.");
            try { if (condition()) return; }
            catch (Exception error) when (error is ElementNotAvailableException or COMException)
            { lastError = error.GetType().Name; }
            Thread.Sleep(100);
        }
        log.Write("uia-timeout", new { child.Process.Id, description, lastError });
        try
        {
            var owned = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, child.Process.Id),
                new PropertyCondition(AutomationElement.NameProperty, "Dango 控制中心")));
            if (owned is not null) LogOwnedBodyState(child, owned, log, "timeout-owned-window");
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException)
        { log.Write("owned-window-diagnostic-unavailable", new { type = error.GetType().Name }); }
        Assert.Fail("UI Automation did not observe " + description + "; see retained process logs.");
    }

    private static void LogOwnedBodyState(ReviewProcess child, AutomationElement window, VerificationLog log, string phase)
    {
        Assert.AreEqual(child.Process.Id, window.Current.ProcessId);
        var controls = window.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(element => element.Current.ControlType == ControlType.Button ||
                element.Current.ControlType == ControlType.Text ||
                element.Current.ControlType == ControlType.Slider)
            .Select(element => new
            {
                element.Current.Name,
                type = element.Current.ControlType.ProgrammaticName,
                element.Current.IsEnabled,
                element.Current.IsOffscreen,
            }).ToArray();
        log.Write("owned-body-state", new { child.Process.Id, phase, controls });
    }

    private sealed class ReviewProcess : IDisposable
    {
        private readonly VerificationLog log;
        private readonly string stdout;
        private readonly string stderr;
        public Process Process { get; }
        private ReviewProcess(Process process, string label, string root, VerificationLog log)
        {
            Process = process; this.log = log;
            stdout = Path.Combine(root, label + ".stdout.log"); stderr = Path.Combine(root, label + ".stderr.log");
            File.WriteAllText(stdout, string.Empty); File.WriteAllText(stderr, string.Empty);
            process.OutputDataReceived += (_, output) => Append(stdout, output.Data);
            process.ErrorDataReceived += (_, output) => Append(stderr, output.Data);
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        public static ReviewProcess Start(string reviewRoot, string label, string evidenceRoot, VerificationLog log)
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Dango.Desktop.exe"))
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("--face-review"); info.ArgumentList.Add("--face-review-settings"); info.ArgumentList.Add(reviewRoot);
            var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("The owned review process could not start.");
            log.Write("owned-process-started", new { label, process.Id });
            try { return new(process, label, evidenceRoot, log); }
            catch
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: false); process.WaitForExit(10_000); }
                process.Dispose();
                throw;
            }
        }
        public void StopNormally(AutomationElement window)
        {
            Assert.AreEqual(Process.Id, window.Current.ProcessId);
            var handle = new nint(window.Current.NativeWindowHandle);
            var threadId = GetWindowThreadProcessId(handle, out var ownerPid);
            Assert.AreEqual((uint)Process.Id, ownerPid, "Shutdown must target only this test's own app process.");
            Assert.IsTrue(threadId != 0 && PostThreadMessage(threadId, 0x0012, 0, 0), "Unable to post WM_QUIT to the owned app UI thread.");
            Assert.IsTrue(Process.WaitForExit(10_000), "The owned app process did not exit normally.");
            Process.WaitForExit(); // The process has exited; drain redirected output before retaining the logs.
            log.Write("owned-process-normal-exit", new { Process.Id, Process.ExitCode });
        }
        public void Dispose()
        {
            if (!Process.HasExited)
            {
                log.Write("owned-process-fallback-termination", new { Process.Id });
                Process.Kill(entireProcessTree: false);
                Process.WaitForExit(10_000);
            }
            if (Process.HasExited) Process.WaitForExit();
            Process.Dispose();
        }
        private void Append(string path, string? text)
        {
            if (text is null) return;
            lock (log) File.AppendAllText(path, text + Environment.NewLine);
        }
    }

    private sealed class VerificationLog : IDisposable
    {
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly StreamWriter writer;
        public VerificationLog(string path) => writer = new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        public void Write(string action, object details)
        { lock (this) writer.WriteLine(JsonSerializer.Serialize(new { seconds = elapsed.Elapsed.TotalSeconds, action, details })); }
        public void Dispose() { lock (this) writer.Dispose(); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
}
