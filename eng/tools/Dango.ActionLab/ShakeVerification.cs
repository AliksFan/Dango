using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Actions;
using Dango.Physics;

namespace Dango.ActionLab;

internal static class ShakeVerification
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    internal static int Run(string module, string output, string? baselineModule)
    {
        string? directory = null;
        var cases = new List<object>();
        try
        {
            directory = ActionLabVerification.ValidateOutput(output); Directory.CreateDirectory(directory);
            _ = new Application();
            using var panel = new ActionLabPanel(module);
            using var source = new HwndSource(new HwndSourceParameters("Dango hidden internal shake verification")
            { Width = 1144, Height = 720, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = panel };
            ActionLabVerification.Layout(panel, 1144, 720);
            Require(!IsWindowVisible(source.Handle), "Verification surface became visible.");
            var regionMotions = new List<RegionMotion>();
            foreach (var candidate in ActionLabCandidates.For(DangoActionKind.Shake))
            {
                var motion = MeasureRegions(module, candidate.ShakeStrength, candidate.ShakeProfile);
                regionMotions.Add(motion);
                cases.Add(new { candidateId = candidate.Id, candidate.ShakeStrength, profile = candidate.ShakeProfile.ToString(), regions = motion });
            }
            Require(regionMotions[1].ShoulderOut < regionMotions[0].ShoulderOut * .9 &&
                regionMotions[1].MiddleRms >= regionMotions[0].MiddleRms * .8 &&
                regionMotions[1].TopRms <= regionMotions[0].TopRms * 1.25,
                "Shoulder candidate did not reduce the upper-side bulge while retaining the burst.");
            foreach (var candidate in ActionLabCandidates.For(DangoActionKind.Shake))
            foreach (var direction in new[] { -1, 1 })
            {
                var candidateDirectory = Path.Combine(directory, candidate.Id);
                Directory.CreateDirectory(candidateDirectory);
                panel.Restart();
                var session = panel.Session;
                var initial = session.Runtime.LastBeltRoomFrame!;
                var peakUpper = 0d; var peakFoot = 0d; var peakGap = 0d; var drift = 0d; var settled = 0d;
                var work = 0d; var torque = 0d; var forces = 0d; var active = 0;
                var maximumStepMilliseconds = 0d; var activeStepMilliseconds = 0d;
                var gif = new GifBitmapEncoder();
                ActionLabVerification.Save(panel, candidateDirectory, "neutral.png", 1144, 720);
                panel.TriggerForVerification(DangoActionKind.Shake, direction, candidate.Id);
                Require(session.Controller.Active, "Actual shake button failed to start.");
                for (var i = 0; i < 240; i++)
                {
                    var watch = Stopwatch.StartNew(); panel.Step(); watch.Stop();
                    Require(session.Error is null, session.Error ?? "Shake faulted.");
                    var frame = session.Runtime.LastBeltRoomFrame!;
                    Require(frame.Tick == initial.Tick + (ulong)i + 1, "Native tick ownership diverged.");
                    var muscles = session.Runtime.ReadActionMuscles();
                    if (muscles.ActiveSubsteps > 0)
                    {
                        active++; work += muscles.Work; torque = Math.Max(torque, muscles.MaximumAbsTorque);
                        forces = Math.Max(forces, Math.Abs(muscles.NetForceX) + Math.Abs(muscles.NetForceY));
                        activeStepMilliseconds += watch.Elapsed.TotalMilliseconds;
                        maximumStepMilliseconds = Math.Max(maximumStepMilliseconds, watch.Elapsed.TotalMilliseconds);
                        Require(muscles.PairCount == 8, "The muscle field did not contain eight real force pairs.");
                        Require(Math.Abs(muscles.Strength - candidate.ShakeStrength) < 1e-6, "The selected candidate used a different muscle strength.");
                        Require(muscles.Profile == candidate.ShakeProfile, "The selected candidate used a different muscle profile.");
                    }
                    Require(frame.BodySnapshot[83] == 0, "Shake used the old top-drag actuator.");
                    peakGap = Math.Max(peakGap, frame.Stats[12]); drift = Math.Max(drift, Math.Abs(frame.Stats[0] - initial.Stats[0]));
                    var upperThisFrame = 0d;
                    for (var p = 0; p < 256; p++)
                    {
                        var distance = Math.Sqrt(Math.Pow(frame.Curve[2*p] - initial.Curve[2*p], 2) + Math.Pow(frame.Curve[2*p+1] - initial.Curve[2*p+1], 2));
                        if (initial.Curve[2*p+1] > initial.Stats[9] + .3 * (initial.Stats[11] - initial.Stats[9])) upperThisFrame = Math.Max(upperThisFrame, distance);
                        if (initial.Curve[2*p+1] < initial.Stats[9] + .035) peakFoot = Math.Max(peakFoot, distance);
                        if (i == 239) settled = Math.Max(settled, distance);
                    }
                    if (upperThisFrame > peakUpper)
                    {
                        peakUpper = upperThisFrame;
                        if (direction == 1) ActionLabVerification.Save(panel, candidateDirectory, "peak.png", 1144, 720);
                    }
                    if (direction == 1 && i < 90 && i % 3 == 0) AddGifFrame(gif, panel);
                }
                Require(active > 30 && !session.Controller.Active, "Shake did not execute a finite muscle burst.");
                Require(peakUpper > .008 && peakFoot < peakUpper * .7 && drift < .02 && peakGap < .003 && settled < .004,
                    $"Body/contact response failed: upper={peakUpper}, foot={peakFoot}, drift={drift}, gap={peakGap}, settled={settled}");
                Require(forces < 1e-10 && torque < 1e-10 && work > 0, "Internal force/work accounting failed.");
                if (direction == 1) { using var stream = File.Create(Path.Combine(candidateDirectory, "shake-simulated-time.gif")); gif.Save(stream); }
                cases.Add(new { candidateId = candidate.Id, candidate.ShakeStrength, direction, activeFrames = active, peakUpper, peakFoot, drift, peakGap, settled, work, forces, torque,
                    averageActiveStepMilliseconds = activeStepMilliseconds / active, maximumStepMilliseconds });
                panel.TriggerForVerification(DangoActionKind.Shake, direction, candidate.Id);
                for (var i = 0; i < 15; i++) panel.Step();
                session.Cancel(); panel.Step();
                Require(session.Runtime.ReadActionMuscles().ActiveSubsteps == 0 && !session.Controller.Active, "Cancel retained muscle activation.");
                session.SetPaused(true); var pausedTick = session.Runtime.LastBeltRoomFrame!.Tick; panel.Step();
                Require(session.Runtime.LastBeltRoomFrame!.Tick == pausedTick, "Pause advanced muscles.");
                session.SetPaused(false); panel.Step();
                Require(session.Runtime.ReadActionMuscles().ActiveSubsteps == 0, "Resume replayed a muscle burst.");
            }
            var compatibleSteps = baselineModule is null ? 0 : CompareOrdinaryPath(module, baselineModule);
            if (baselineModule is not null)
            {
                using var reference = new DangoBeltRoomWorld(baselineModule, DangoBodyMaterial.Configure(DangoBodyControls.Default));
                if (reference.SupportsMuscleActuation)
                {
                    var previousRegions = MeasureRegions(baselineModule, ActionLabCandidates.For(DangoActionKind.Shake)[0].ShakeStrength); var currentRegions = regionMotions[0];
                    cases.Add(new { previousRegions, currentRegions });
                    Require(Math.Abs(currentRegions.Middle - previousRegions.Middle) < 1e-7 &&
                        Math.Abs(currentRegions.Top - previousRegions.Top) < 1e-7 &&
                        Math.Abs(currentRegions.MiddleRms - previousRegions.MiddleRms) < 1e-7 &&
                        Math.Abs(currentRegions.TopRms - previousRegions.TopRms) < 1e-7,
                        "The current candidate diverged from the previous middle-body version.");
                }
            }
            Require(!IsWindowVisible(source.Handle), "Hidden verification became visible.");
            Write(directory, new { passed = true, cases, compatibleSteps, scope = "actual button + native internal muscles + hidden WPF rendering; GIF uses simulation time, not a foreground cadence/aesthetic acceptance" });
            return 0;
        }
        catch (Exception error) { if (directory is not null) Write(directory, new { passed = false, failure = error.Message, cases }); return 1; }
    }

    private static int CompareOrdinaryPath(string module, string baseline)
    {
        var configuration = DangoBodyMaterial.Configure(DangoBodyControls.Default);
        using var current = new DangoBeltRoomWorld(module, configuration, left: -5, right: 5);
        using var previous = new DangoBeltRoomWorld(baseline, configuration, left: -5, right: 5);
        Require(current.SupportsMuscleActuation, "Current module must support internal muscles.");
        var before = previous.Read().Tick;
        if (!previous.SupportsMuscleActuation)
        {
            try { previous.Step(1, new DangoBeltInput { MuscleShakeTime = .2, MuscleShakeStrength = .4 }); throw new InvalidOperationException("Old module silently accepted muscles."); }
            catch (NotSupportedException) { Require(previous.Read().Tick == before, "Unsupported actuation advanced old physics."); }
        }
        else if (!previous.SupportsMuscleProfiles)
        {
            try
            {
                previous.Step(1, new DangoBeltInput { MuscleShakeTime = .2, MuscleShakeStrength = 1.44,
                    MuscleShakeProfile = DangoMuscleShakeProfile.SoftShoulders });
                throw new InvalidOperationException("Old module silently substituted the original muscle profile.");
            }
            catch (NotSupportedException) { Require(previous.Read().Tick == before, "Unsupported profile advanced old physics."); }
        }
        for (var i = 0; i < 360; i++)
        {
            var input = i switch
            {
                >= 120 and < 150 => new DangoBeltInput(Horizontal: .2, InputActive: true),
                >= 170 and < 190 => new DangoBeltInput(Downward: .25, InputActive: true),
                >= 200 and < 220 => new DangoBeltInput(Stretch: .22, InputActive: true),
                235 => new DangoBeltInput(JumpPressed: true, InputActive: true),
                _ => default,
            };
            current.Step(1, input); previous.Step(1, input);
            var a = current.Read(); var b = previous.Read();
            Require(a.BodySnapshot.SequenceEqual(b.BodySnapshot) && a.Curve.SequenceEqual(b.Curve) && a.Stats.SequenceEqual(b.Stats), "An ordinary input diverged from the old module.");
        }
        return 360;
    }

    private sealed record RegionMotion(double Middle, double Top, double Foot, double MiddleRms, double TopRms, double ShoulderUp, double ShoulderOut);
    private static RegionMotion MeasureRegions(string module, double strength, DangoMuscleShakeProfile profile = DangoMuscleShakeProfile.Original)
    {
        using var world = new DangoBeltRoomWorld(module, DangoBodyMaterial.Configure(DangoBodyControls.Default), left: -5, right: 5);
        for (var i = 0; i < 120; i++) world.Step(1);
        var initial = world.Read(); var middle = 0d; var top = 0d; var foot = 0d; var middleRms = 0d; var topRms = 0d;
        var shoulderUp = 0d; var shoulderOut = 0d;
        var outerHalfWidth = .5 * (initial.Stats[10] - initial.Stats[8]);
        for (var i = 0; i < 150; i++)
        {
            world.Step(1, i < 45 ? new DangoBeltInput(InputActive: true) { MuscleShakeTime = (i + 1) / 60d, MuscleShakeStrength = strength, MuscleShakeProfile = profile } : default);
            var frame = world.Read();
            var middleSum = 0d; var topSum = 0d; var middleCount = 0; var topCount = 0;
            for (var p = 0; p < 256; p++)
            {
                var height = (double)(initial.Curve[2*p+1] - initial.Stats[9]) / (initial.Stats[11] - initial.Stats[9]);
                var distance = Math.Sqrt(Math.Pow(frame.Curve[2*p] - initial.Curve[2*p], 2) + Math.Pow(frame.Curve[2*p+1] - initial.Curve[2*p+1], 2));
                if (height is >= .3 and <= .68) { middle = Math.Max(middle, distance); middleSum += distance * distance; middleCount++; }
                if (height >= .78) { top = Math.Max(top, distance); topSum += distance * distance; topCount++; }
                var initialSide = initial.Curve[2*p] - initial.Stats[0];
                if (height is >= .6 and <= .9 && Math.Abs(initialSide) > outerHalfWidth * .25)
                {
                    shoulderUp = Math.Max(shoulderUp, frame.Curve[2*p+1] - initial.Curve[2*p+1]);
                    shoulderOut = Math.Max(shoulderOut, Math.Sign(initialSide) * (frame.Curve[2*p] - initial.Curve[2*p]));
                }
                if (initial.Curve[2*p+1] < initial.Stats[9] + .035) foot = Math.Max(foot, distance);
            }
            middleRms = Math.Max(middleRms, Math.Sqrt(middleSum / middleCount));
            topRms = Math.Max(topRms, Math.Sqrt(topSum / topCount));
        }
        return new(middle, top, foot, middleRms, topRms, shoulderUp, shoulderOut);
    }

    private static void AddGifFrame(GifBitmapEncoder gif, ActionLabPanel panel)
    {
        var bitmap = new RenderTargetBitmap(1144, 720, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen()) dc.DrawRectangle((Brush)panel.FindResource("HomeBackground"), null, new Rect(0,0,1144,720));
        bitmap.Render(backdrop); bitmap.Render(panel);
        var metadata = new BitmapMetadata("gif"); metadata.SetQuery("/grctlext/Delay", (ushort)5); metadata.SetQuery("/grctlext/Disposal", (byte)2);
        gif.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
    }
    private static void Write(string directory, object data) => File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
