using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Rendering.Actions;

namespace Dango.ActionLab;

/// <summary>Real native integration and actual WPF button wiring, on a hidden HWND.</summary>
internal static class ActionLabVerification
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    internal static int Run(string module, string output)
    {
        string? directory = null;
        var reports = new List<object>();
        var layoutReports = new List<object>();
        var failures = new List<string>();
        try
        {
            directory = ValidateOutput(output);
            Directory.CreateDirectory(directory);
            _ = new Application();
            using var panel = new ActionLabPanel(module);
            using var source = new HwndSource(new HwndSourceParameters("Dango hidden action verification")
            { Width = 1144, Height = 720, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = panel };
            Layout(panel, 1144, 720);
            Require(!IsWindowVisible(source.Handle), "Verification window became visible.");
            var expectedButtons = ActionLabPanel.Actions.Sum(a => ActionLabCandidates.For(a.Kind).Count);
            var expectedCases = expectedButtons * 2;
            Require(panel.ActionButtons.Count == ActionLabPanel.Actions.Length, "The experiment must expose every action.");
            Require(panel.ShowForces.IsChecked != true, "Force overlay must default off.");
            CheckForceToggle(panel);
            Save(panel, directory, "overview.png", 1144, 720);
            Require(panel.CandidateButtons.Count == expectedButtons, "Candidate buttons do not match the catalog.");
            foreach (var showForces in new[] { false, true })
            foreach (var (width, height, label) in new[] { (1044, 660, "minimum"), (1144, 720, "common"), (1044, 900, "portrait") })
            {
                panel.ShowForces.IsChecked = showForces; panel.Render();
                var suffix = showForces ? "-forces" : "";
                Layout(panel, width, height);
                CheckButtons(panel, width, height, requireScrolling: label == "minimum");
                layoutReports.Add(new { label, showForces, width, height, extentHeight = panel.CandidateScroll.ExtentHeight,
                    viewportHeight = panel.CandidateScroll.ViewportHeight, bottomOffset = panel.CandidateScroll.VerticalOffset });
                Save(panel, directory, label + suffix + "-bottom.png", width, height);
                panel.CandidateScroll.ScrollToTop(); panel.UpdateLayout();
                Save(panel, directory, label + suffix + "-top.png", width, height);
            }
            Layout(panel, 1144, 720);

            foreach (var direction in new[] { -1, 1 })
            foreach (var (kind, name, _) in ActionLabPanel.Actions)
            foreach (var candidate in ActionLabCandidates.For(kind))
            {
                panel.Restart();
                var session = panel.Session;
                var initial = session.Runtime.LastBeltRoomFrame!;
                var start = session.Observe();
                var nativeStart = initial.Tick;
                var startHeight = initial.Stats[11] - initial.Stats[9];
                var minimumHeight = (double)startHeight; var maximumHeight = (double)startHeight;
                var peakGap = 0d; var maxPenetration = 0d; var maxTopShift = 0d; var peakMiddle = 0d;
                var initialEye = session.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left;
                var minEyeOpenness = 1d; var maxEyeShift = 0d;
                var airborne = false; var jumpAccepted = false; var steps = 0;
                var peakForceArrows = 0; var forceSources = new HashSet<int>(); var impulseLabel = false;
                panel.CandidateButtons[(kind, candidate.Id)].BringIntoView(); panel.UpdateLayout();
                CheckReachable(panel.CandidateButtons[(kind, candidate.Id)], panel, 1144, 720);
                panel.TriggerForVerification(kind, direction, candidate.Id);
                Require(session.Controller.Active, $"Button failed to start {name}/{candidate.Id}: {session.Phase}");
                Require(session.LastCandidateId == candidate.Id, "Button selected a different candidate.");
                Require(panel.ForceOverlay.Arrows.Count == 0, "Starting action retained the previous ledger.");
                Require(panel.CandidateButtons.Values.All(b => !b.IsEnabled), "Busy scene left candidate buttons enabled.");
                Require(panel.CandidateButtons.Count(b => Equals(b.Value.Tag, "Selected")) == 1 &&
                    Equals(panel.CandidateButtons[(kind, candidate.Id)].Tag, "Selected"), "Playing highlight does not identify exactly this candidate.");
                for (; steps < 370 && session.Controller.Active && session.Error is null; steps++)
                {
                    panel.Step();
                    var frame = session.Runtime.LastBeltRoomFrame!;
                    var observation = session.Observe();
                    VerifyForceArrows(panel);
                    peakForceArrows = Math.Max(peakForceArrows, panel.ForceOverlay.Arrows.Count);
                    foreach (var arrow in panel.ForceOverlay.Arrows) forceSources.Add(arrow.Force.SourceKind);
                    if (session.Controller.Active && kind is (DangoActionKind.SlowBlink or DangoActionKind.Squint or DangoActionKind.LookAway or DangoActionKind.LookBack))
                    {
                        Require(panel.ForceOverlay.Arrows.Count == 0, "Eye pose drew a fake force arrow.");
                        Require(panel.ForceNote.Contains("眼部姿态指令，无力箭头"), "Eye pose did not explain its non-force channel.");
                    }
                    if (kind == DangoActionKind.ChargedHop && frame.Stats[15] != 0)
                    {
                        Require(panel.ForceOverlay.Arrows.Count == 0 && panel.ForceNote.Contains("瞬时冲量，非持续力"),
                            "Jump impulse was represented as a continuing force.");
                        impulseLabel = true;
                    }
                    if (steps == 24) CheckForceToggle(panel);
                    minimumHeight = Math.Min(minimumHeight, frame.Stats[11] - frame.Stats[9]);
                    maximumHeight = Math.Max(maximumHeight, frame.Stats[11] - frame.Stats[9]);
                    peakGap = Math.Max(peakGap, frame.Stats[12]);
                    maxPenetration = Math.Max(maxPenetration, frame.Stats[31]);
                    maxTopShift = Math.Max(maxTopShift, Math.Abs(observation.TopX - start.TopX));
                    if (kind == DangoActionKind.Shake)
                        for (var p = 0; p < 256; p++)
                        {
                            var height = (double)(initial.Curve[2*p+1] - initial.Stats[9]) / (initial.Stats[11] - initial.Stats[9]);
                            if (height is >= .3 and <= .68)
                                peakMiddle = Math.Max(peakMiddle, Math.Sqrt(Math.Pow(frame.Curve[2*p] - initial.Curve[2*p], 2) + Math.Pow(frame.Curve[2*p+1] - initial.Curve[2*p+1], 2)));
                        }
                    var eye = session.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left;
                    minEyeOpenness = Math.Min(minEyeOpenness, eye.Openness);
                    maxEyeShift = Math.Max(maxEyeShift, Math.Abs(eye.Center.X - initialEye.Center.X));
                    airborne |= !observation.Grounded && frame.Stats[12] > .003;
                    jumpAccepted |= frame.Stats[15] != 0;
                    if (direction == 1 && steps == 24) Save(panel, directory, kind + "-" + candidate.Id + "-right.png", 1144, 720);
                }
                var last = session.Runtime.LastBeltRoomFrame!;
                var moved = session.Observe().CoreX - start.CoreX;
                reports.Add(new { action = kind.ToString(), name, candidateId = candidate.Id, strength = kind == DangoActionKind.Shake ? candidate.ShakeStrength : (double?)null,
                    profile = kind == DangoActionKind.Shake ? candidate.ShakeProfile.ToString() : null, direction, steps, nativeSteps = last.Tick - nativeStart,
                    error = session.Error, controllerError = session.Controller.Error, moved, minimumHeight, maximumHeight,
                    startHeight, peakGap, maxPenetration, maxTopShift, peakMiddle, minEyeOpenness, maxEyeShift, airborne, jumpAccepted,
                    peakForceArrows, forceSources = forceSources.Order().ToArray(), impulseLabel });
                void Check(bool condition, string message) { if (!condition) failures.Add($"{kind}/{candidate.Id}/{direction}: {message}"); }
                Check(session.Error is null && !session.Controller.Active, $"Action failed: {session.Error ?? "timeout"}");
                Check(panel.CandidateButtons.Values.All(b => b.IsEnabled), "Completed scene did not re-enable all candidates.");
                Check(panel.CandidateButtons.Values.All(b => b.Tag is null), "Completed action retained a playing highlight.");
                Check(panel.ForceOverlay.Arrows.Count == 0 && panel.ForceOverlay.Frame is null, "Completed action retained its force ledger.");
                if (kind is not (DangoActionKind.SlowBlink or DangoActionKind.Squint or DangoActionKind.LookAway or DangoActionKind.LookBack))
                    Check(peakForceArrows > 0, "Body action did not display any real active forces.");
                Check(last.Tick - nativeStart == (ulong)steps, "Native step ownership diverged.");
                Check(maxPenetration < .003, $"Ground penetration: {maxPenetration}");
                if (kind == DangoActionKind.ChargedHop)
                {
                    Check(airborne && jumpAccepted && session.Observe().Grounded, "Hop did not lift off and land.");
                    Check(impulseLabel, "Hop did not identify its instantaneous impulse.");
                    Check(peakGap is > .02 and < .35, $"Light hop exceeded its experiment range: {peakGap}");
                }
                if (kind == DangoActionKind.Shake) Check(peakMiddle > .008, "Shake did not physically deform its middle region.");
                if (kind == DangoActionKind.SlowBlink) Check(minEyeOpenness < .1, "Slow blink did not close real rendered eyes.");
                if (kind == DangoActionKind.Squint) Check(minEyeOpenness < .6, "Squint did not narrow real rendered eyes.");
                if (kind is DangoActionKind.LookAway or DangoActionKind.LookBack) Check(maxEyeShift > .005, "Gaze did not move real rendered eyes.");
                if (kind == DangoActionKind.Approach) Check(moved * direction > .15, "Approach did not physically move.");
                if (kind is DangoActionKind.Retreat or DangoActionKind.Recoil) Check(moved * direction < -.005, "Retreat did not physically move.");
                if (kind is DangoActionKind.Sway or DangoActionKind.Peek or DangoActionKind.Lean)
                    Check(maxTopShift > .0005, "The body did not physically deform.");
                if (kind == DangoActionKind.Lower) Check(minimumHeight < startHeight - .002, "Lower did not physically compress.");
                if (kind == DangoActionKind.Stretch) Check(maximumHeight > startHeight + .002, "Stretch did not physically extend.");
                for (var i = 0; i < 90; i++) panel.Step();
                Check(session.Error is null, $"Post-action settling failed: {session.Error}");
                if (kind is DangoActionKind.SlowBlink or DangoActionKind.Squint or DangoActionKind.LookAway or DangoActionKind.LookBack or DangoActionKind.Peek)
                {
                    var restoredEye = session.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left;
                    Check(restoredEye.Openness > .95 && Math.Abs(restoredEye.Center.X - initialEye.Center.X) < .005, "Owned eye channels did not restore neutral state.");
                }
                Require(!IsWindowVisible(source.Handle), "Hidden surface became visible during actions.");
            }
            // Same button path, then cancellation and minimize-style pause: no held
            // force, no queued replay, and no native advancement during the pause.
            panel.Restart(); panel.TriggerForVerification(DangoActionKind.Sway, 1); panel.Step();
            Require(panel.StopButton.IsEnabled, "Stop was disabled while playing.");
            panel.StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!panel.Session.Controller.Active, "Cancel retained an action.");
            Require(panel.ForceOverlay.Frame is null && panel.ForceOverlay.Arrows.Count == 0, "Cancel retained force arrows.");
            panel.Step();
            Require(panel.Session.Runtime.LastBeltRoomFrame!.BodySnapshot[83] == 0, "Cancel retained the native drag force.");
            var pausedAt = panel.Session.Runtime.LastBeltRoomFrame!.Tick;
            // Pause an active candidate to prove it is cancelled rather than resumed.
            panel.TriggerForVerification(DangoActionKind.Shake, -1, "soft"); panel.Step();
            pausedAt = panel.Session.Runtime.LastBeltRoomFrame!.Tick;
            panel.Session.SetPaused(true); panel.Render();
            Require(panel.CandidateButtons.Values.All(b => !b.IsEnabled), "Paused scene left actions enabled.");
            Require(panel.ForceOverlay.Frame is null && panel.ForceOverlay.Arrows.Count == 0, "Pause retained force arrows.");
            Require(panel.RestartButton.IsEnabled, "Paused scene hid recovery.");
            for (var i = 0; i < 20; i++) panel.Step();
            Require(panel.Session.Runtime.LastBeltRoomFrame!.Tick == pausedAt, "Paused scene advanced.");
            panel.Session.SetPaused(false); panel.Step();
            Require(!panel.Session.Controller.Active && panel.Session.Runtime.LastBeltRoomFrame!.Tick == pausedAt + 1, "Resume replayed an action or skipped a step.");
            Require(panel.CandidateButtons.Values.All(b => b.IsEnabled), "Resume did not re-enable candidates.");
            panel.TriggerForVerification(DangoActionKind.Squint, 1);
            for (var i = 0; i < 40; i++) panel.Step();
            panel.Session.Cancel();
            for (var i = 0; i < 60; i++) panel.Step();
            Require(panel.Session.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness > .95, "Cancel retained an eye pose.");
            // Exercise the controller's actual invalid-time fault gate, then the
            // session's error propagation. This is not a native-fault simulation.
            panel.TriggerForVerification(DangoActionKind.Shake, 1, "soft");
            panel.Session.Controller.Advance(double.NaN, panel.Session.Observe()); panel.Step();
            Require(panel.Session.Error is not null && !panel.Session.Controller.Active, "Controller fault did not stop the session.");
            Require(panel.CandidateButtons.Values.All(b => !b.IsEnabled), "Fault left actions enabled.");
            Require(panel.ForceOverlay.Frame is null && panel.ForceOverlay.Arrows.Count == 0, "Fault retained force arrows.");
            var faultAt = panel.Session.Runtime.LastBeltRoomFrame!.Tick;
            for (var i = 0; i < 20; i++) panel.Step();
            Require(panel.Session.Runtime.LastBeltRoomFrame!.Tick == faultAt, "Faulted scene kept advancing.");
            CheckReachable(panel.RestartButton, panel, 1144, 720);
            Require(panel.RestartButton.IsEnabled, "Fault disabled recovery.");
            panel.RestartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(panel.Session.Error is null && panel.CandidateButtons.Values.All(b => b.IsEnabled), "Rebuild did not recover the session.");
            // Exercise real startup failure/retry, using only a new experiment-owned
            // module path. No missing-file error is allowed to close the review UI.
            var recoveryModule = Path.Combine(directory, "recovery-module.dll");
            using var recovery = new ActionLabHost(recoveryModule);
            Require(recovery.Panel is null && recovery.Error is not null && recovery.RetryButton is not null, "Startup failure did not expose retry.");
            File.Copy(module, recoveryModule);
            Require(recovery.RetryButton!.IsEnabled, "Startup retry is disabled.");
            recovery.RetryButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Require(recovery.Panel is not null && recovery.Error is null, "Retry did not recover the real native scene.");
            Require(reports.Count == expectedCases, "Candidate/direction matrix did not cover the catalog completely.");
            Write(directory, new { passed = failures.Count == 0, failures, hidden = true, actionCount = ActionLabPanel.Actions.Length, buttonCount = expectedButtons, expectedCases, cases = reports,
                layoutSizes = new[] { "1044x660 minimum client", "1144x720 common client", "1044x900 portrait client" },
                layouts = layoutReports,
                scrolling = "Every candidate brought into view at all three sizes; ancestor clips and fixed tools checked at every position.",
                faultRecovery = "Controller invalid-time fault stops the session; all candidates disable; rebuild recovers. Missing native module startup/retry also covered.",
                forceOverlay = "Read-only last-substep active-force ledger; every nonzero entry checked for provenance, host origin, vector direction and fixed 90 DIP/N length. Toggle does not step physics. Eye pose and jump impulse labels, cancellation/pause/fault/completion clearing covered. Excludes gravity, elasticity, damping, contacts and constraint reactions.",
                moduleSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
                scope = "deterministic real native steps + hidden WPF buttons/rendering; visible input, wall-clock cadence and aesthetics remain unverified" });
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            if (directory is not null) Write(directory, new { passed = false, failure = error.Message, cases = reports, layouts = layoutReports });
            return 1;
        }
    }

    private static void CheckForceToggle(ActionLabPanel panel)
    {
        var session = panel.Session;
        var steps = session.Steps;
        var tick = session.Runtime.LastBeltRoomFrame!.Tick;
        var elapsed = session.Controller.Elapsed;
        panel.ShowForces.IsChecked = false; panel.Render();
        Require(panel.ForceOverlay.Arrows.Count == 0 && panel.ForceOverlay.Frame is null, "Hiding forces retained the ledger.");
        panel.ShowForces.IsChecked = true; panel.Render();
        Require(session.Steps == steps && session.Runtime.LastBeltRoomFrame!.Tick == tick && session.Controller.Elapsed == elapsed,
            "Force overlay toggle advanced physical or action time.");
    }

    private static void VerifyForceArrows(ActionLabPanel panel)
    {
        var session = panel.Session;
        if (!session.Controller.Active || session.Paused || session.Error is not null)
        {
            Require(panel.ForceOverlay.Frame is null && panel.ForceOverlay.Arrows.Count == 0, "Inactive scene retained force arrows.");
            return;
        }
        var ledger = session.Runtime.ReadActionForces();
        var displayed = panel.ForceOverlay.Frame;
        Require(displayed is not null && displayed.Attempt == ledger.Attempt && displayed.Substep == ledger.Substep &&
            displayed.Forces.SequenceEqual(ledger.Forces), "Overlay did not use the actual successful native force ledger.");
        var nonzero = ledger.Forces.Where(f => f.ForceX != 0 || f.ForceY != 0).ToArray();
        Require(panel.ForceOverlay.Arrows.Select(a => a.Force).SequenceEqual(nonzero), "Displayed arrows lost or invented a force entry.");
        foreach (var arrow in panel.ForceOverlay.Arrows)
        {
            var origin = session.Runtime.ActionForceToHost(arrow.Force.X, arrow.Force.Y);
            Require(Math.Abs(arrow.Start.X - origin.X) < 1e-6 && Math.Abs(arrow.Start.Y - origin.Y) < 1e-6,
                "Force origin does not match the native application point.");
            // The preview conversion is +X right, +Y up. Force length uses the
            // declared fixed scale, independently of the world's meter scale.
            var vector = arrow.End - arrow.Start;
            Require(Math.Abs(vector.X - arrow.Force.ForceX * ActionForceOverlay.DipPerNewton) < 1e-6 &&
                Math.Abs(vector.Y + arrow.Force.ForceY * ActionForceOverlay.DipPerNewton) < 1e-6,
                "Arrow direction or fixed force scale diverged from the native vector.");
        }
    }

    internal static void Layout(ActionLabPanel panel, double width, double height)
    { panel.Measure(new(width, height)); panel.Arrange(new(0, 0, width, height)); panel.UpdateLayout(); }

    private static void CheckButtons(ActionLabPanel panel, double width, double height, bool requireScrolling)
    {
        panel.CandidateScroll.ScrollToTop(); panel.UpdateLayout();
        Require(panel.CandidateScroll.ScrollableWidth == 0, "Candidate list requires horizontal scrolling.");
        var preview = panel.PreviewWorld.TransformToAncestor(panel).TransformBounds(new Rect(panel.PreviewWorld.RenderSize));
        Require(preview.Width >= 520 - .5 && preview.Height >= 340 - .5, "Layout shrank the experiment preview below its natural size.");
        foreach (var button in panel.CandidateButtons.Values)
        {
            button.BringIntoView(); panel.UpdateLayout();
            CheckReachable(button, panel, width, height);
            CheckReachable(panel.StopButton, panel, width, height);
            CheckReachable(panel.RestartButton, panel, width, height);
            CheckReachable(panel.ShowForces, panel, width, height, minimumHeight: 32);
        }
        // Read settled extent/viewport after BringIntoView has completed layout;
        // a taller layout can fit all rows and correctly remain at offset zero.
        var needsScrolling = panel.CandidateScroll.ExtentHeight > panel.CandidateScroll.ViewportHeight + .5;
        if (requireScrolling) Require(needsScrolling, "Minimum layout did not exercise vertical scrolling.");
        if (needsScrolling)
        {
            // BringIntoView need not move the viewport when only trailing margin
            // overflows. Exercise the actual scroll extent explicitly as well.
            panel.CandidateScroll.ScrollToBottom(); panel.UpdateLayout();
            Require(panel.CandidateScroll.VerticalOffset > 0 &&
                Math.Abs(panel.CandidateScroll.VerticalOffset - panel.CandidateScroll.ScrollableHeight) <= .5,
                "Candidate list did not reach its actual bottom scroll position.");
            CheckReachable(panel.CandidateButtons.Values.Last(), panel, width, height);
            CheckReachable(panel.StopButton, panel, width, height);
            CheckReachable(panel.RestartButton, panel, width, height);
        }
    }

    private static void CheckReachable(FrameworkElement control, ActionLabPanel panel, double width, double height, double minimumHeight = 40)
    {
        var bounds = control.TransformToAncestor(panel).TransformBounds(new Rect(control.RenderSize));
        Require(bounds.Right <= width + .5 && bounds.Bottom <= height + .5 && bounds.X >= -.5 && bounds.Y >= -.5,
            "Button leaves the experiment bounds: " + System.Windows.Automation.AutomationProperties.GetName(control));
        Require(control.ActualHeight >= minimumHeight && control.ActualWidth >= 40, "Button hit area is too small.");
        for (var ancestor = VisualTreeHelper.GetParent(control); ancestor is Visual visual; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            var local = control.TransformToAncestor(visual).TransformBounds(new Rect(control.RenderSize));
            if (ancestor is FrameworkElement element && (element.ClipToBounds || element is ScrollContentPresenter))
                Require(Contains(new Rect(element.RenderSize), local), "Button is clipped by a scrolling/layout ancestor.");
            if (VisualTreeHelper.GetClip(visual) is { } clip)
                Require(Contains(clip.Bounds, local), "Button is clipped by an ancestor geometry.");
            if (ReferenceEquals(ancestor, panel)) break;
        }
        static bool Contains(Rect outer, Rect inner) => inner.X >= outer.X - .5 && inner.Y >= outer.Y - .5 &&
            inner.Right <= outer.Right + .5 && inner.Bottom <= outer.Bottom + .5;
    }

    internal static void Save(ActionLabPanel panel, string directory, string name, int width, int height)
    {
        panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle((Brush)panel.FindResource("HomeBackground"), null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(panel);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }

    internal static string ValidateOutput(string output)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Dango.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidDataException("Cannot locate experiment workspace.");
        var allowed = Path.GetFullPath(Path.Combine(root.FullName, "artifacts", "action-lab"));
        var path = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);
        if (!path.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.Exists(path))
            throw new InvalidDataException("Verification needs a new artifacts/action-lab subdirectory.");
        for (var parent = new DirectoryInfo(path).Parent; parent is not null; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Output must not traverse a reparse point.");
        return path;
    }

    private static void Write(string directory, object report) => File.WriteAllText(Path.Combine(directory, "result.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
