using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
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
        var failures = new List<string>();
        try
        {
            directory = ValidateOutput(output);
            Directory.CreateDirectory(directory);
            _ = new Application();
            using var panel = new ActionLabPanel(module);
            using var source = new HwndSource(new HwndSourceParameters("Dango hidden action verification")
            { Width = 1040, Height = 720, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = panel };
            Layout(panel, 1040, 720);
            Require(!IsWindowVisible(source.Handle), "Verification window became visible.");
            Require(panel.ActionButtons.Count == 14, "The experiment must expose exactly 14 actions.");
            Save(panel, directory, "overview.png", 1040, 720);
            Layout(panel, 924, 660); CheckButtons(panel, 924, 660);
            Save(panel, directory, "minimum-layout.png", 924, 660);
            Layout(panel, 1040, 720);

            foreach (var direction in new[] { -1, 1 })
            foreach (var (kind, name, _) in ActionLabPanel.Actions)
            {
                panel.Restart();
                var session = panel.Session;
                var initial = session.Runtime.LastBeltRoomFrame!;
                var start = session.Observe();
                var nativeStart = initial.Tick;
                var startHeight = initial.Stats[11] - initial.Stats[9];
                var minimumHeight = (double)startHeight; var maximumHeight = (double)startHeight;
                var peakGap = 0d; var maxPenetration = 0d; var maxTopShift = 0d;
                var initialEye = session.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left;
                var minEyeOpenness = 1d; var maxEyeShift = 0d;
                var airborne = false; var jumpAccepted = false; var steps = 0;
                panel.TriggerForVerification(kind, direction);
                Require(session.Controller.Active, $"Button failed to start {name}: {session.Phase}");
                for (; steps < 370 && session.Controller.Active && session.Error is null; steps++)
                {
                    panel.Step();
                    var frame = session.Runtime.LastBeltRoomFrame!;
                    var observation = session.Observe();
                    minimumHeight = Math.Min(minimumHeight, frame.Stats[11] - frame.Stats[9]);
                    maximumHeight = Math.Max(maximumHeight, frame.Stats[11] - frame.Stats[9]);
                    peakGap = Math.Max(peakGap, frame.Stats[12]);
                    maxPenetration = Math.Max(maxPenetration, frame.Stats[31]);
                    maxTopShift = Math.Max(maxTopShift, Math.Abs(observation.TopX - start.TopX));
                    var eye = session.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left;
                    minEyeOpenness = Math.Min(minEyeOpenness, eye.Openness);
                    maxEyeShift = Math.Max(maxEyeShift, Math.Abs(eye.Center.X - initialEye.Center.X));
                    airborne |= !observation.Grounded && frame.Stats[12] > .003;
                    jumpAccepted |= frame.Stats[15] != 0;
                    if (direction == 1 && steps == 24) Save(panel, directory, kind + ".png", 1040, 720);
                }
                var last = session.Runtime.LastBeltRoomFrame!;
                var moved = session.Observe().CoreX - start.CoreX;
                reports.Add(new { action = kind.ToString(), name, direction, steps, nativeSteps = last.Tick - nativeStart,
                    error = session.Error, controllerError = session.Controller.Error, moved, minimumHeight, maximumHeight,
                    startHeight, peakGap, maxPenetration, maxTopShift, minEyeOpenness, maxEyeShift, airborne, jumpAccepted });
                void Check(bool condition, string message) { if (!condition) failures.Add($"{kind}/{direction}: {message}"); }
                Check(session.Error is null && !session.Controller.Active, $"Action failed: {session.Error ?? "timeout"}");
                Check(last.Tick - nativeStart == (ulong)steps, "Native step ownership diverged.");
                Check(maxPenetration < .003, $"Ground penetration: {maxPenetration}");
                if (kind == DangoActionKind.ChargedHop)
                {
                    Check(airborne && jumpAccepted && session.Observe().Grounded, "Hop did not lift off and land.");
                    Check(peakGap is > .02 and < .35, $"Light hop exceeded its experiment range: {peakGap}");
                }
                if (kind == DangoActionKind.Shake) Check(maxTopShift > .008, "Shake response is too small for review.");
                if (kind == DangoActionKind.SlowBlink) Check(minEyeOpenness < .1, "Slow blink did not close real rendered eyes.");
                if (kind == DangoActionKind.Squint) Check(minEyeOpenness < .6, "Squint did not narrow real rendered eyes.");
                if (kind is DangoActionKind.LookAway or DangoActionKind.LookBack) Check(maxEyeShift > .005, "Gaze did not move real rendered eyes.");
                if (kind == DangoActionKind.Approach) Check(moved * direction > .15, "Approach did not physically move.");
                if (kind is DangoActionKind.Retreat or DangoActionKind.Recoil) Check(moved * direction < -.005, "Retreat did not physically move.");
                if (kind is DangoActionKind.Shake or DangoActionKind.Sway or DangoActionKind.Peek or DangoActionKind.Lean)
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
            panel.Session.Cancel(); Require(!panel.Session.Controller.Active, "Cancel retained an action.");
            panel.Step();
            Require(panel.Session.Runtime.LastBeltRoomFrame!.BodySnapshot[83] == 0, "Cancel retained the native drag force.");
            var pausedAt = panel.Session.Runtime.LastBeltRoomFrame!.Tick;
            panel.Session.SetPaused(true);
            for (var i = 0; i < 20; i++) panel.Step();
            Require(panel.Session.Runtime.LastBeltRoomFrame!.Tick == pausedAt, "Paused scene advanced.");
            panel.Session.SetPaused(false); panel.Step();
            Require(!panel.Session.Controller.Active && panel.Session.Runtime.LastBeltRoomFrame!.Tick == pausedAt + 1, "Resume replayed an action or skipped a step.");
            panel.TriggerForVerification(DangoActionKind.Squint, 1);
            for (var i = 0; i < 40; i++) panel.Step();
            panel.Session.Cancel();
            for (var i = 0; i < 60; i++) panel.Step();
            Require(panel.Session.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness > .95, "Cancel retained an eye pose.");
            // Exercise real startup failure/retry, using only a new experiment-owned
            // module path. No missing-file error is allowed to close the review UI.
            var recoveryModule = Path.Combine(directory, "recovery-module.dll");
            using var recovery = new ActionLabHost(recoveryModule);
            Require(recovery.Panel is null && recovery.Error is not null && recovery.RetryButton is not null, "Startup failure did not expose retry.");
            File.Copy(module, recoveryModule);
            recovery.RetryButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Require(recovery.Panel is not null && recovery.Error is null, "Retry did not recover the real native scene.");
            Write(directory, new { passed = failures.Count == 0, failures, hidden = true, buttonCount = 14, cases = reports,
                moduleSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
                scope = "deterministic real native steps + hidden WPF buttons/rendering; visible input, wall-clock cadence and aesthetics remain unverified" });
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            if (directory is not null) Write(directory, new { passed = false, failure = error.Message, cases = reports });
            return 1;
        }
    }

    private static void Layout(ActionLabPanel panel, double width, double height)
    { panel.Measure(new(width, height)); panel.Arrange(new(0, 0, width, height)); panel.UpdateLayout(); }

    private static void CheckButtons(ActionLabPanel panel, double width, double height)
    {
        foreach (var button in panel.AllButtons)
        {
            var bounds = button.TransformToAncestor(panel).TransformBounds(new Rect(button.RenderSize));
            Require(bounds.Right <= width && bounds.Bottom <= height && bounds.X >= 0 && bounds.Y >= 0, "Action button was clipped.");
            Require(button.ActualHeight >= 40, "Button hit area is too short.");
        }
    }

    private static void Save(ActionLabPanel panel, string directory, string name, int width, int height)
    {
        panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle((Brush)panel.FindResource("HomeBackground"), null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(panel);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }

    private static string ValidateOutput(string output)
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
