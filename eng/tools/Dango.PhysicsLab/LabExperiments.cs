using System.Security.Cryptography;
using System.IO;
using System.Text.Json;
using Dango.Physics;
using Dango.Physics.Configuration;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

public static class LabExperiments
{
    private static readonly double Dt = TimeSpan.FromSeconds(1d / 60).TotalSeconds;
    private sealed record Phase(string Name, int Frames, double Horizontal = 0, double Downward = 0,
        double Stretch = 0, bool Active = false, bool JumpFirst = false, bool CancelFirst = false);
    private sealed record Observation(double CoreX, double CoreY, double ComX, double ComY, double Width,
        double Height, double Speed, bool Contact, double MinimumNodeBottom);
    private sealed record WalkMeasure(string Configuration, DangoLabDrive Mode, double Bias, string Direction,
        double CoreRms, double ComRms, double Distance);

    public static void Run(string module, string session)
    {
        Directory.CreateDirectory(session);
        var results = new List<object>();
        var walkMeasures = new List<WalkMeasure>();
        var contracts = new List<object>();
        var failures = new List<Exception>();
        var configurations = new[] {
            (Name: "default", Controls: new DangoBodyControls()),
            (Name: "80-0-0", Controls: new DangoBodyControls(80, 0, 0)),
            (Name: "120-100-0", Controls: new DangoBodyControls(120, 100, 0)),
        };
        var drives = new[] { (DangoLabDrive.Core, 0d), (DangoLabDrive.Mass, 0d),
            (DangoLabDrive.HeightBias, 1d), (DangoLabDrive.HeightBias, -1d),
            (DangoLabDrive.PointHeight, .5d), (DangoLabDrive.PointHeight, -.5d) };
        foreach (var config in configurations)
        {
            var parameters = DangoBodyMaterial.Configure(config.Controls);
            foreach (var (mode, bias) in drives)
                Experiment(config.Name, parameters, mode, bias, "walk", [
                    new("rest", 300), new("right", 600, Horizontal: 1, Active: true),
                    new("release-right", 180), new("left", 600, Horizontal: -1, Active: true), new("release-left", 180)]);
            Experiment(config.Name, parameters, DangoLabDrive.Core, 0, "actions", [
                new("rest",300), new("W",60,Stretch:1,Active:true),new("W-release",60),
                new("S",60,Downward:1,Active:true),new("S-release",60),new("WS",60,Active:true,CancelFirst:true),
                new("AD",60,Active:true),new("Space",90,JumpFirst:true),
                new("jump-W",90,Stretch:1,Active:true,JumpFirst:true),new("release",60),
                new("jump-S",90,Downward:1,Active:true,JumpFirst:true),new("release",60),
                new("W-before-cancel",60,Stretch:1,Active:true),new("cancel",30,CancelFirst:true)]);
            Contract(config.Name + ":neutral-bitwise", () => CompareOldPath(parameters, jump: false));
            Contract(config.Name + ":Space-bitwise", () => CompareOldPath(parameters, jump: true));
            Contract(config.Name + ":W-force-budget-release-cancel", () => CheckStretch(parameters));
        }
        var report = new {
            SchemaVersion = 1, FixedDt = Dt, Seed = "deterministic-no-random", ModuleSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
            FloorBounds = new { Left = -256, Right = 256, RequiredHorizontalMargin = 2,
                Oracle = "every frame: core +/- maximum node distance +/- node radius stays within floor with 2 world margin; finite world XY and node bottoms; node bottom cannot escape more than two node radii beneath floor" },
            MaterialVersion = DangoBodyMaterial.ModelVersion, GaitEnabled = false,
            Scope = "isolated physical experiment; not adopted product behavior or full endpoint/manual acceptance",
            Spectrum = "last 480 samples per phase; demeaned rectangular DFT; 0.5 to 5 Hz RMS; null when phase shorter than 480",
            Configurations = configurations.Select(c => new { c.Name, c.Controls, Parameters = DangoBodyMaterial.Configure(c.Controls) }),
            DirectionComparisons = walkMeasures.Where(m => m.Mode != DangoLabDrive.Core).Select(m => {
                var baseline = walkMeasures.FirstOrDefault(b => b.Configuration == m.Configuration && b.Direction == m.Direction && b.Mode == DangoLabDrive.Core);
                return new { m.Configuration, Mode = m.Mode.ToString(), m.Bias, m.Direction,
                    CoreRmsRatioToCore = Ratio(m.CoreRms, baseline?.CoreRms), MassComRmsRatioToCore = Ratio(m.ComRms, baseline?.ComRms),
                    DistanceRatioToCore = Ratio(Math.Abs(m.Distance), baseline is null ? null : Math.Abs(baseline.Distance)) };
            }),
            Contracts = contracts, Experiments = results,
        };
        File.WriteAllText(Path.Combine(session, "experiment-results.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count != 0) throw new AggregateException("Physical laboratory safety contracts failed; report saved.", failures);

        void Contract(string name, Action action)
        {
            try { action(); contracts.Add(new { Name = name, Passed = true, Error = (string?)null }); }
            catch (Exception error) { contracts.Add(new { Name = name, Passed = false, Error = error.Message }); failures.Add(error); }
            SaveCheckpoint();
        }

        void SaveCheckpoint() => File.WriteAllText(Path.Combine(session, "experiment-progress.json"),
            JsonSerializer.Serialize(new { Status = "incomplete-until-final-report", FloorLeft = -256, FloorRight = 256,
                Experiments = results, Contracts = contracts }, new JsonSerializerOptions { WriteIndented = true }));

        void Experiment(string configuration, DangoSelfPhysicsParameters parameters, DangoLabDrive mode, double bias,
            string sequence, Phase[] phases)
        {
            var summaries = new List<object>();
            string? failure = null;
            try
            {
                using var world = DangoPhysicsWorld.Create(module, 1, parameters);
                world.RequireLab();
                var id = world.Add(0, 0);
                world.SetHorizontalBounds(-256, 256);
                foreach (var phase in phases)
                {
                    var observations = new List<Observation>(phase.Frames);
                    var longestGap = 0;
                    var gap = 0;
                    for (var i = 0; i < phase.Frames; i++)
                    {
                        var frame = world.StepLab(Dt, [new(id, phase.Horizontal, phase.Downward,
                            JumpPressed: i == 0 && phase.JumpFirst, Stretch: phase.Stretch,
                            InputActive: phase.Active || (i == 0 && phase.JumpFirst), CancelForces: i == 0 && phase.CancelFirst)], mode, bias);
                        if (!frame.Succeeded) throw new InvalidOperationException($"{phase.Name} frame {i}: native {frame.ErrorCode}");
                        var body = frame.Bodies.Single();
                        var nodes = world.ReadLabNodes(id);
                        var minimumNodeBottom = VerifyGeometry(body, nodes, parameters);
                        var mass = nodes.Sum(n => (double)n.Mass);
                        var comX = nodes.Sum(n => n.X * (double)n.Mass) / mass;
                        var comY = nodes.Sum(n => n.Y * (double)n.Mass) / mass;
                        var xs = body.Contour.Where((_, n) => n % 2 == 0).Select(v => (double)v).ToArray();
                        var ys = body.Contour.Where((_, n) => n % 2 != 0).Select(v => (double)v).ToArray();
                        observations.Add(new(body.CoreX, body.CoreY, comX, comY, xs.Max() - xs.Min(),
                            ys.Max() - ys.Min(), body.MaximumSpeed, body.GroundContactCount > 0, minimumNodeBottom));
                        gap = body.GroundContactCount == 0 ? gap + 1 : 0;
                        longestGap = Math.Max(longestGap, gap);
                    }
                    var first = observations[0];
                    var last = observations[^1];
                    var coreRms = Rms(observations.Select(o => o.CoreY).ToArray());
                    var comRms = Rms(observations.Select(o => o.ComY).ToArray());
                    if (sequence == "walk" && phase.Horizontal != 0)
                        walkMeasures.Add(new(configuration, mode, bias, phase.Name, coreRms, comRms, last.ComX - first.ComX));
                    summaries.Add(new {
                        phase.Name, phase.Frames, phase.Horizontal, phase.Downward, phase.Stretch, phase.Active,
                        AverageWidth = observations.Average(o => o.Width), AverageHeight = observations.Average(o => o.Height),
                        CoreYRms = coreRms, MassComYRms = comRms,
                        CoreYBandRms = BandRms(observations.Select(o => o.CoreY).ToArray()),
                        MassComYBandRms = BandRms(observations.Select(o => o.ComY).ToArray()),
                        ZeroContactFrames = observations.Count(o => !o.Contact), LongestZeroContactFrames = longestGap,
                        MinimumNodeBottom = observations.Min(o => o.MinimumNodeBottom), GroundHeight = parameters.GroundHeight,
                        CoreDisplacementX = last.CoreX - first.CoreX, MassComDisplacementX = last.ComX - first.ComX,
                        MeanMaximumNodeSpeed = observations.Average(o => o.Speed), PeakNodeSpeed = observations.Max(o => o.Speed),
                        EndWidth = last.Width, EndHeight = last.Height,
                        TailMeanHeight = observations.TakeLast(Math.Min(30, phase.Frames)).Average(o => o.Height),
                        EndCoreX = last.CoreX, EndMassComX = last.ComX,
                    });
                }
            }
            catch (Exception error) { failure = error.Message; }
            results.Add(new { Configuration = configuration, Mode = mode.ToString(), Bias = bias, Sequence = sequence,
                Completed = failure is null, Error = failure, Phases = summaries });
            SaveCheckpoint();
            Console.WriteLine($"{configuration} {mode} {bias:+0.0;-0.0;0} {sequence}: {(failure ?? "completed; geometry oracle passed")}");
        }

        void CompareOldPath(DangoSelfPhysicsParameters parameters, bool jump)
        {
            using var oldWorld = DangoPhysicsWorld.Create(module, 1, parameters);
            using var labWorld = DangoPhysicsWorld.Create(module, 1, parameters);
            var oldId = oldWorld.Add(0, 0);
            var labId = labWorld.Add(0, 0);
            oldWorld.SetHorizontalBounds(-256, 256);
            labWorld.SetHorizontalBounds(-256, 256);
            for (var i = 0; i < 480; i++)
            {
                var pressed = jump && i == 300;
                var old = oldWorld.Step(TimeSpan.FromSeconds(Dt), [new(oldId, JumpPressed: pressed)]);
                var lab = labWorld.StepLab(Dt, [new(labId, JumpPressed: pressed, InputActive: pressed)], DangoLabDrive.Core);
                if (!old.Succeeded || !lab.Succeeded) throw new InvalidOperationException($"native fault at {i}");
                // Records compare contour by reference, so compare every native float and integer field explicitly.
                var a = old.Bodies.Single(); var b = lab.Bodies.Single();
                VerifyGeometry(a, oldWorld.ReadLabNodes(oldId), parameters);
                VerifyGeometry(b, labWorld.ReadLabNodes(labId), parameters);
                var fa = a.Contour.Concat([a.CoreX,a.CoreY,a.SignedArea,a.CentroidX,a.CentroidY,a.MaximumSpeed]).Select(BitConverter.SingleToInt32Bits);
                var fb = b.Contour.Concat([b.CoreX,b.CoreY,b.SignedArea,b.CentroidX,b.CentroidY,b.MaximumSpeed]).Select(BitConverter.SingleToInt32Bits);
                if (!fa.SequenceEqual(fb) || a.GroundContactCount != b.GroundContactCount || a.IsGrounded != b.IsGrounded ||
                    a.SelectedDragNode != b.SelectedDragNode || a.PointCount != b.PointCount || old.ContactStepCount != lab.ContactStepCount)
                    throw new InvalidOperationException($"old step/lab core mismatch at frame {i}, jump={pressed}");
            }
        }

        void CheckStretch(DangoSelfPhysicsParameters parameters)
        {
            using var world = DangoPhysicsWorld.Create(module, 1, parameters);
            var id = world.Add(0, 0);
            world.SetHorizontalBounds(-256, 256);
            for (var i = 0; i < 300; i++) Step(new(id));
            for (var i = 0; i < 60; i++)
            {
                Step(new(id, Stretch: 1, InputActive: true));
                var s = world.ReadLabStats(id);
                var tolerance = Math.Max(1e-5, Math.Abs(s.MaximumStretchBudget) * 1e-5);
                if (Math.Abs(s.StretchNetForceX) > tolerance || Math.Abs(s.StretchNetForceY) > tolerance ||
                    Math.Abs(s.StretchTorqueAboutCore) > tolerance || s.StretchForceBudgetUsed > s.MaximumStretchBudget + tolerance)
                    throw new InvalidOperationException($"W force/torque/budget contract failed: {s}");
            }
            Step(new(id));
            if (world.ReadLabStats(id).StretchRamp <= 0) throw new InvalidOperationException("ordinary W release skipped decay");
            for (var i = 0; i < 30; i++) Step(new(id));
            if (world.ReadLabStats(id).StretchRamp != 0) throw new InvalidOperationException("W release did not reach zero");
            for (var i = 0; i < 15; i++) Step(new(id, Stretch: 1, InputActive: true));
            Step(new(id, CancelForces: true));
            if (world.ReadLabStats(id).StretchRamp != 0) throw new InvalidOperationException("cancel did not immediately clear ramp");
            void Step(DangoLabInput input)
            {
                var frame = world.StepLab(Dt, [input], DangoLabDrive.Core);
                if (!frame.Succeeded) throw new InvalidOperationException($"native contract fault {frame.ErrorCode}");
                VerifyGeometry(frame.Bodies.Single(), world.ReadLabNodes(id), parameters);
            }
        }
    }

    private static double? Ratio(double value, double? reference) => reference is > 0 ? value / reference.Value : null;

    private static double VerifyGeometry(DangoBodyState body, IReadOnlyList<DangoLabNode> nodes, DangoSelfPhysicsParameters parameters)
    {
        if (!float.IsFinite(body.CoreX) || !float.IsFinite(body.CoreY) || nodes.Count < 2 ||
            nodes.Any(n => !float.IsFinite(n.X) || !float.IsFinite(n.Y) || !float.IsFinite(n.VelocityX) ||
                !float.IsFinite(n.VelocityY) || !float.IsFinite(n.Mass) || n.Mass <= 0))
            throw new InvalidOperationException("Non-finite physical world coordinates, velocities or invalid mass.");
        var ring = nodes.Where(n => n.Index != 0).ToArray();
        var maximumRadius = ring.Max(n => Math.Sqrt(Math.Pow(n.X - body.CoreX, 2) + Math.Pow(n.Y - body.CoreY, 2)));
        var extent = maximumRadius + parameters.NodeRadius;
        if (body.CoreX - extent < -254 || body.CoreX + extent > 254)
            throw new InvalidOperationException("Floor-extent oracle failed: body lacks two-world horizontal margin; case is not comparable.");
        var bottom = ring.Min(n => n.Y - parameters.NodeRadius);
        if (!double.IsFinite(bottom) || bottom < parameters.GroundHeight - 2 * parameters.NodeRadius)
            throw new InvalidOperationException($"Floor escape oracle failed: bottom={bottom}, floor={parameters.GroundHeight}.");
        return bottom;
    }

    private static double Rms(double[] values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    private static double? BandRms(double[] values)
    {
        const int n = 480;
        if (values.Length < n) return null;
        var data = values[^n..];
        var mean = data.Average();
        double power = 0;
        for (var k = 1; k < n / 2; k++)
        {
            var frequency = k / (n * Dt);
            if (frequency < .5 || frequency > 5) continue;
            double re = 0, im = 0;
            for (var j = 0; j < n; j++)
            {
                var angle = 2 * Math.PI * k * j / n;
                re += (data[j] - mean) * Math.Cos(angle);
                im += (data[j] - mean) * Math.Sin(angle);
            }
            power += 2 * (re * re + im * im) / (n * n);
        }
        return Math.Sqrt(power);
    }
}
