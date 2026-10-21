using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Actual app processes and Windows UI Automation, using only an owned fictional review directory.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AccessoryProcessRestartTests
{
    private const string MarkerName = ".dango-local-install-acceptance.json";
    private const string Marker = "{\"application\":\"Dango\",\"acceptanceFormat\":1,\"fictional\":true}";
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("ProcessIntegration")]
    public void RealExecutableAppliesThroughUiAndSecondProcessRestoresAfterSourceMove()
    {
        var runRoot = NewRunRoot("restart");
        var reviewRoot = Path.Combine(runRoot, "review");
        Directory.CreateDirectory(reviewRoot);
        File.WriteAllText(Path.Combine(reviewRoot, MarkerName), Marker);
        var logPath = Path.Combine(runRoot, "process-events.jsonl");
        using var log = new VerificationLog(logPath);
        ReviewProcess? first = null;
        ReviewProcess? duplicate = null;
        ReviewProcess? second = null;
        try
        {
            AssertRuntimeFiles();
            AssertReviewMutexIsFree(reviewRoot);
            var mainSettings = ControlCenterWindowSettings.CreateDefault() with
            {
                IntroSeen = true,
                DisplayMode = Dango.Desktop.DesktopHost.DesktopDisplayMode.Window,
            };
            new ControlCenterWindowSettingsStore().Save(Path.Combine(reviewRoot, "settings.json"), mainSettings);
            var source = CreateFictionalPng(runRoot);
            var sourceBytes = File.ReadAllBytes(source);
            var session = new AccessorySettingsSession(reviewRoot);
            var imported = session.ImportPng(source) with
            {
                Name = "虚构进程配饰 A",
                BodyAnchorX = .27,
                BodyAnchorY = .16,
                SizePercent = 117,
                BaseAngleDegrees = 19,
            };
            session.Stage(new([imported]));
            Assert.IsTrue(session.TrySave());
            var movedSource = Path.Combine(runRoot, "moved-fictional-source.png");
            File.Move(source, movedSource);
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(movedSource));
            Assert.IsFalse(File.Exists(source));
            log.Write("fixture-saved-source-moved", new { imported.Id, imported.AssetId, imported.Name });

            first = ReviewProcess.Start(reviewRoot, "first", runRoot, log);
            var firstWindow = WaitForMainWindow(first, log);
            Invoke(WaitForControl(first, firstWindow, "Q · 换装", ControlType.Button, log), log, "open-wardrobe-first");
            SelectLibraryItem(first, firstWindow, imported.Name, log);
            OpenPlacementSection(first, firstWindow, log, "open-placement-first");
            var initialRange = GetRange(WaitForControl(first, firstWindow, "身体附着 X", ControlType.Slider, log));
            Assert.AreEqual(.27, initialRange.Current.Value, 1e-9);
            log.Write("first-process-restored-source-independent", new { first.Process.Id, bodyAnchorX = initialRange.Current.Value });

            duplicate = ReviewProcess.Start(reviewRoot, "duplicate", runRoot, log);
            Assert.IsTrue(duplicate.Process.WaitForExit(10_000), "The duplicate review process did not exit after the mutex rejection.");
            Assert.AreEqual(0, duplicate.Process.ExitCode, "The existing isolated review instance must receive the activation signal.");
            Assert.IsFalse(first.Process.HasExited, "The primary review process must survive duplicate rejection.");
            log.Write("duplicate-review-rejected", new { primaryPid = first.Process.Id, duplicatePid = duplicate.Process.Id, duplicate.Process.ExitCode });

            const double changedAnchor = .73;
            var anchorInput = WaitForControl(first, firstWindow, "身体附着 X 数值", ControlType.Edit, log);
            anchorInput.SetFocus();
            Assert.IsTrue(anchorInput.TryGetCurrentPattern(ValuePattern.Pattern, out var inputPattern), "The actual numeric input must expose ValuePattern.");
            ((ValuePattern)inputPattern).SetValue("0.73");
            WaitUntil(first, () => ((ValuePattern)anchorInput.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "0.73",
                "focused numeric input update", log);
            log.Write("first-process-focused-numeric-input", new { first.Process.Id, text = "0.73" });
            Invoke(WaitForControl(first, firstWindow, "配饰：柔性与连接", ControlType.Button, log), log, "open-material-first");
            foreach (var (name, text) in new[] { ("最高允许形变量 % 数值", "175"), ("连接最大行程 % 数值", "150") })
            {
                var input = WaitForControl(first, firstWindow, name, ControlType.Edit, log);
                input.SetFocus();
                ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
            }
            var folding = WaitForControl(first, firstWindow, "允许翻折", ControlType.CheckBox, log);
            ((TogglePattern)folding.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
            WaitUntil(first, () => ((TogglePattern)folding.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On,
                "folding option checked", log);
            LogOwnedWardrobeState(first, firstWindow, log, "before-apply");
            Invoke(WaitForControl(first, firstWindow, "应用", ControlType.Button, log), log, "apply-first-process");
            LogOwnedWardrobeState(first, firstWindow, log, "after-apply-invocation");
            // Do not open the target while the asynchronous UIA click is replacing it:
            // a FileShare.Read poll can itself block the Windows atomic overwrite.
            WaitUntil(first, () => FindControl(firstWindow, "配饰已保存到本机。", ControlType.Text) is not null &&
                FindControl(firstWindow, "应用", ControlType.Button) is { Current.IsEnabled: false } &&
                FindControl(firstWindow, "连接最大行程 %", ControlType.Slider) is { } slider &&
                Math.Abs(GetRange(slider).Current.Value - 150) < 1e-9,
                "first process UI save completion", log);
            var committed = new AccessorySettingsStore().Load(reviewRoot);
            Assert.AreEqual(AccessorySettingsLoadState.Valid, committed.State);
            Assert.AreEqual(changedAnchor, committed.Configuration.Items.Single(item => item.Id == imported.Id).BodyAnchorX, 1e-9,
                "The UI save result must be independently confirmed on disk.");
            var committedItem = committed.Configuration.Items.Single(item => item.Id == imported.Id);
            Assert.IsTrue(committedItem.AllowFolding);
            Assert.AreEqual(175d, committedItem.MaxDeformationPercent);
            Assert.AreEqual(150d, committedItem.MaxConnectionTravelPercent);
            Assert.AreEqual("sprout", new ControlCenterWindowSettingsStore().LoadOrDefault(Path.Combine(reviewRoot, "settings.json")).Settings.HatPresetId);
            log.Write("first-process-ui-commit-confirmed", new { first.Process.Id, bodyAnchorX = changedAnchor });
            first.StopNormally(firstWindow);
            Assert.AreEqual(0, first.Process.ExitCode);
            var firstPid = first.Process.Id;
            first.Dispose(); first = null;

            second = ReviewProcess.Start(reviewRoot, "second", runRoot, log);
            Assert.AreNotEqual(firstPid, second.Process.Id, "Restart must create an actual different app process.");
            var secondWindow = WaitForMainWindow(second, log);
            Invoke(WaitForControl(second, secondWindow, "Q · 换装", ControlType.Button, log), log, "open-wardrobe-second");
            SelectLibraryItem(second, secondWindow, imported.Name, log);
            OpenPlacementSection(second, secondWindow, log, "open-placement-second");
            var restored = GetRange(WaitForControl(second, secondWindow, "身体附着 X", ControlType.Slider, log)).Current.Value;
            Assert.AreEqual(changedAnchor, restored, 1e-9);
            Invoke(WaitForControl(second, secondWindow, "配饰：柔性与连接", ControlType.Button, log), log, "open-material-second");
            Assert.AreEqual(175d, GetRange(WaitForControl(second, secondWindow, "最高允许形变量 %", ControlType.Slider, log)).Current.Value);
            Assert.AreEqual(150d, GetRange(WaitForControl(second, secondWindow, "连接最大行程 %", ControlType.Slider, log)).Current.Value);
            Assert.AreEqual(ToggleState.On, ((TogglePattern)WaitForControl(second, secondWindow, "允许翻折", ControlType.CheckBox, log)
                .GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
            Assert.IsFalse(File.Exists(source));
            Assert.IsTrue(File.Exists(AccessorySettingsStore.GetAssetPath(reviewRoot, imported.AssetId)));
            log.Write("second-process-ui-restored", new { second.Process.Id, bodyAnchorX = restored, imported.Name });
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
            duplicate?.Dispose(); second?.Dispose(); first?.Dispose();
            log.Write("retained-owned-verification-data", new { runRoot, reviewRoot });
            TestContext.WriteLine("Owned process restart evidence: " + runRoot);
            TestContext.AddResultFile(logPath);
        }
    }

    [TestMethod]
    public void OwnedProductAcceptanceUsesAnIndependentDirectoryMutexAndRejectsRetiredArguments()
    {
        var root = NewRunRoot("owned-instance");
        File.WriteAllText(Path.Combine(root, MarkerName), Marker);
        var args = new[] { "--local-install-acceptance-settings", root };
        App.ValidateStartupArguments(args);
        Assert.AreNotEqual(App.ResolveSingleInstanceName([]), App.ResolveSingleInstanceName(args));
        Assert.AreEqual(Path.Combine(root, "settings.json"), Resolve(root));
        foreach (var retired in new[] { "--accessory-review", "--accessory-review-settings" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => App.ValidateStartupArguments([retired]));
            Assert.ThrowsExactly<ArgumentException>(() => App.ResolveSingleInstanceName([retired]));
        }
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveLocalInstallAcceptanceSettingsFile(
            ["--local-install-acceptance-settings", root, "--eye-hat-review"]));
    }

    [TestMethod]
    public void ReviewRejectsUnownedDailyPackageRelativeAndDriveRootsWithoutWritingThem()
    {
        var unowned = NewRunRoot("unowned");
        Assert.ThrowsExactly<FileNotFoundException>(() => Resolve(unowned));
        var daily = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango");
        foreach (var invalid in new[] { daily, daily + ".", daily + " ", Path.Combine(daily, "fictional-review"),
            Path.GetDirectoryName(daily)!, AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "fictional-review"),
            "fictional-relative-review", Path.GetPathRoot(TestRepository.Root)! })
            Assert.ThrowsExactly<InvalidDataException>(() => Resolve(invalid));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(unowned).Length);
    }

    [TestMethod]
    [DataRow("{\"application\":\"Other\",\"acceptanceFormat\":1,\"fictional\":true}")]
    [DataRow("{\"application\":\"Dango\",\"acceptanceFormat\":2,\"fictional\":true}")]
    [DataRow("{\"application\":\"Dango\",\"acceptanceFormat\":1,\"fictional\":false}")]
    [DataRow("{\"application\":\"Dango\",\"acceptanceFormat\":1,\"fictional\":true,\"extra\":true}")]
    public void ReviewRejectsWrongOwnershipMarkers(string marker)
    {
        var root = NewRunRoot("wrong-marker");
        File.WriteAllText(Path.Combine(root, MarkerName), marker);
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(root));
        Assert.AreEqual(marker, File.ReadAllText(Path.Combine(root, MarkerName)));
    }

    [TestMethod]
    [DataRow("settings.json")]
    [DataRow("settings.window-preset.json")]
    [DataRow("appearance.json")]
    [DataRow("accessories.json")]
    [DataRow(MarkerName)]
    public void ReviewRejectsDirectoryAtASettingsOrMarkerFile(string fileName)
    {
        var root = NewRunRoot("directory-rejection");
        if (fileName != MarkerName) File.WriteAllText(Path.Combine(root, MarkerName), Marker);
        Directory.CreateDirectory(Path.Combine(root, fileName));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(root));
    }

    private static string? Resolve(string root) => App.ResolveLocalInstallAcceptanceSettingsFile(["--local-install-acceptance-settings", root]);

    private static string NewRunRoot(string label)
    {
        var root = Path.GetFullPath(Path.Combine(TestRepository.Root, "artifacts", "accessory-verification", "process", Guid.NewGuid().ToString("N") + "-" + label));
        Assert.IsTrue(root.StartsWith(Path.GetFullPath(TestRepository.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFictionalPng(string root)
    {
        var path = Path.Combine(root, "fictional-source.png");
        var pixels = new byte[6 * 4 * 4];
        for (var y = 1; y < 4; y++)
            for (var x = 1; x < 5; x++)
            {
                var index = (y * 6 + x) * 4;
                pixels[index] = 170; pixels[index + 1] = 80; pixels[index + 2] = 35; pixels[index + 3] = 255;
            }
        var bitmap = BitmapSource.Create(6, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 24);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
        return path;
    }

    private static string ProductPackageDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("DANGO_PRODUCT_PACKAGE");
            if (string.IsNullOrWhiteSpace(configured)) return AppContext.BaseDirectory;
            Assert.IsTrue(Path.IsPathFullyQualified(configured), "DANGO_PRODUCT_PACKAGE must identify an absolute product package directory.");
            return Path.GetFullPath(configured);
        }
    }

    private static void AssertRuntimeFiles()
    {
        var package = ProductPackageDirectory;
        foreach (var name in new[] { "Dango.Desktop.exe", "Dango.Desktop.runtimeconfig.json", "dango_belt_product.dll", "dango_belt_room.dll", "dango_box2d.dll" })
            Assert.IsTrue(File.Exists(Path.Combine(package, name)), "The real process acceptance requires output file " + name);
    }

    private static void AssertReviewMutexIsFree(string root)
    {
        try
        {
            using var existing = Mutex.OpenExisting(App.ResolveSingleInstanceName(["--local-install-acceptance-settings", root]) + ".Mutex");
            Assert.Fail("An owned product acceptance instance is already active. This test must not activate or automate an unrelated review window.");
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    private static AutomationElement WaitForMainWindow(ReviewProcess child, VerificationLog log)
    {
        AutomationElement? window = null;
        WaitUntil(child, () =>
        {
            // Process.MainWindowHandle can identify the scene window during startup.
            // Only the owned product control center contains the wardrobe entry.
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
        WaitUntil(child, () => (result = FindControl(window, name, type)) is not null && result.Current.IsEnabled,
            "UIA control " + name, log);
        Assert.AreEqual(child.Process.Id, result!.Current.ProcessId, "UIA must stay within the app process created by this test.");
        return result;
    }

    private static void SelectLibraryItem(ReviewProcess child, AutomationElement window, string name, VerificationLog log)
    {
        var list = WaitForControl(child, window, "已佩戴配饰", ControlType.List, log);
        AutomationElement? row = null;
        WaitUntil(child, () =>
        {
            var rows = list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            row = rows.Cast<AutomationElement>().SingleOrDefault(item => item.Current.Name.Contains(name, StringComparison.Ordinal));
            return rows.Count == 1 && row is not null;
        }, "restored accessory library row " + name, log);
        Assert.IsTrue(row!.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern), "The real library row must expose SelectionItemPattern.");
        ((SelectionItemPattern)pattern).Select();
        log.Write("uia-library-selected", new { child.Process.Id, rowName = row.Current.Name });
    }

    private static void OpenPlacementSection(ReviewProcess child, AutomationElement window, VerificationLog log, string action)
    {
        var button = WaitForControl(child, window, "配饰：图像与位置", ControlType.Button, log);
        Invoke(button, log, action);
        WaitUntil(child, () => button.Current.ItemStatus == "已选", "actual placement section selected", log);
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
            if (owned is not null) LogOwnedWardrobeState(child, owned, log, "timeout-owned-window");
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException)
        { log.Write("owned-window-diagnostic-unavailable", new { type = error.GetType().Name }); }
        Assert.Fail("UI Automation did not observe " + description + "; see retained process logs.");
    }

    private static void LogOwnedWardrobeState(ReviewProcess child, AutomationElement window, VerificationLog log, string phase)
    {
        Assert.AreEqual(child.Process.Id, window.Current.ProcessId);
        var controls = window.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(element => element.Current.ControlType == ControlType.Text ||
                element.Current.ControlType == ControlType.Button || element.Current.ControlType == ControlType.CheckBox ||
                element.Current.Name is "配饰名称" or "身体附着 X 数值" or "身体附着 X" or
                    "最高允许形变量 % 数值" or "最高允许形变量 %" or "连接最大行程 % 数值" or "连接最大行程 %")
            .Select(element =>
            {
                string? value = null;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var edit)) value = ((ValuePattern)edit).Current.Value;
                if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var slider)) value = ((RangeValuePattern)slider).Current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new
                {
                    element.Current.Name,
                    type = element.Current.ControlType.ProgrammaticName,
                    element.Current.IsEnabled,
                    element.Current.IsOffscreen,
                    element.Current.HasKeyboardFocus,
                    value
                };
            }).ToArray();
        log.Write("owned-wardrobe-state", new { child.Process.Id, phase, controls });
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
            var package = ProductPackageDirectory;
            var info = new ProcessStartInfo(Path.Combine(package, "Dango.Desktop.exe"))
            {
                WorkingDirectory = package,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("--local-install-acceptance-settings"); info.ArgumentList.Add(reviewRoot);
            var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("The owned review process could not start.");
            log.Write("owned-process-started", new { label, process.Id, package });
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
