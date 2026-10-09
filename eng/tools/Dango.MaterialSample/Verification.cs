using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;

namespace Dango.MaterialSample;

internal sealed record CheckResult(string Name, bool Passed, object Evidence);

internal static class Verification
{
    internal static int Run(string library, string output)
    {
        var results = new List<CheckResult>();
        var landed = new Dictionary<string, ShapeMetrics>();
        var flown = new Dictionary<string, ShapeMetrics>();
        void Check(string name, Action action)
        {
            try { action(); }
            catch (Exception error) { results.Add(new(name, false, new { error = error.Message })); }
            Console.WriteLine($"{name}: {(results[^1].Passed ? "PASS" : "FAIL")}");
        }

        foreach (var size in new[] { 80d, 100d, 120d })
        foreach (var softness in new[] { 0d, 50d, 100d })
        {
            var key = $"size-{size}-soft-{softness}";
            var controls = new MaterialControls(size, softness);
            Check(key + "-flight", () =>
            {
                using var session = new MaterialSession(library, controls.Configure(), height: 8);
                var maxSpread = 0d; var maxAspectError = 0d; var minimumCircularity = 1d;
                for (var i = 0; i < 120; i++)
                {
                    var state = session.Step(); var metrics = ShapeMetrics.Measure(state);
                    Require(!state.IsGrounded && state.GroundContactCount == 0 &&
                        metrics.LowestWorldY > session.Configuration.GroundHeight + .05, "飞行窗口发生接触。");
                    if (i >= 60)
                    {
                        maxSpread = Math.Max(maxSpread, metrics.RadialSpread);
                        maxAspectError = Math.Max(maxAspectError, Math.Abs(metrics.Aspect - 1));
                        minimumCircularity = Math.Min(minimumCircularity, metrics.Circularity);
                        Require(metrics.IsRound, $"飞行圆形失败：径向差={metrics.RadialSpread:F5}，宽高比={metrics.Aspect:F5}，圆度={metrics.Circularity:F5}。");
                    }
                }
                flown[key] = ShapeMetrics.Measure(session.State!);
                results.Add(new(key + "-flight", true, new { maxSpread, maxAspectError, minimumCircularity }));
            });
            Check(key + "-landing", () =>
            {
                using var session = new MaterialSession(library, controls.Configure());
                var tail = new List<(double X, double Y, ShapeMetrics Shape)>();
                var everGrounded = false;
                for (var i = 0; i < 600; i++)
                {
                    var state = session.Step(); var shape = ShapeMetrics.Measure(state);
                    everGrounded |= state.IsGrounded;
                    Require(shape.LowestWorldY >= session.Configuration.GroundHeight - .002, "轮廓穿过地面。");
                    if (i >= 480)
                    {
                        Require(state.IsGrounded && state.MaximumSpeed < .03, $"落地未停稳：速度={state.MaximumSpeed:F5}。");
                        tail.Add((state.CoreX, state.CoreY, shape));
                    }
                }
                var xDrift = tail.Max(p => p.X) - tail.Min(p => p.X);
                var yDrift = tail.Max(p => p.Y) - tail.Min(p => p.Y);
                var widthDrift = tail.Max(p => p.Shape.Width) - tail.Min(p => p.Shape.Width);
                var heightDrift = tail.Max(p => p.Shape.Height) - tail.Min(p => p.Shape.Height);
                Require(everGrounded && new[] { xDrift, yDrift, widthDrift, heightDrift }.Max() < .003,
                    "落地核心或轮廓持续漂移。");
                landed[key] = tail[^1].Shape;
                results.Add(new(key + "-landing", true, new { xDrift, yDrift, widthDrift, heightDrift, shape = tail[^1].Shape }));
            });
            Check(key + "-recovery", () =>
            {
                var trace = Recover(library, controls, requireRound: true);
                results.Add(new(key + "-recovery", true, trace));
            });
        }

        Check("size-changes-real-flight-diameter", () =>
        {
            var standard = flown["size-100-soft-50"].Width;
            var small = flown["size-80-soft-50"].Width / standard;
            var large = flown["size-120-soft-50"].Width / standard;
            Require(Math.Abs(small - .8) <= .01 && Math.Abs(large - 1.2) <= .01,
                "大小没有按比例改变真实物理轮廓。");
            results.Add(new("size-changes-real-flight-diameter", true, new { small, large }));
        });

        Check("softness-changes-grounded-shape", () =>
        {
            var hard = landed["size-100-soft-0"]; var soft = landed["size-100-soft-100"];
            Require(soft.Aspect - hard.Aspect >= .03 || hard.Height - soft.Height >= .03 * 2 * .68,
                "软硬端点没有产生足够的实际落地形态差异。");
            results.Add(new("softness-changes-grounded-shape", true, new { hard, soft }));
        });

        Check("damping-changes-real-recovery-trajectory", () =>
        {
            var low = Recover(library, new(Settling: 0), requireRound: true);
            var high = Recover(library, new(Settling: 100), requireRound: true);
            var maximumDifference = low.Spread.Zip(high.Spread, (a, b) => Math.Abs(a - b)).Max();
            Require(maximumDifference >= .01, "阻尼端点没有改变实际恢复轨迹。");
            results.Add(new("damping-changes-real-recovery-trajectory", true, new { maximumDifference,
                lowRecoverySeconds = low.RecoverySeconds, highRecoverySeconds = high.RecoverySeconds }));
        });

        Check("round-metric-positive-and-negative-controls", () =>
        {
            // Pure metric fixtures, explicitly not simulated bodies or physical evidence.
            var circle = Enumerable.Range(0, 16).SelectMany(i =>
            {
                var t = 2 * Math.PI * i / 16; var angle = t + .06 * Math.Sin(t);
                return new[] { (float)(.725 * Math.Cos(angle)), (float)(.725 * Math.Sin(angle)) };
            }).ToArray();
            var fixture = new DangoBodyState(new(1), circle, 0, 10, 1.6f,
                Enumerable.Range(0, 16).Average(i => circle[2 * i]),
                Enumerable.Range(0, 16).Average(i => circle[2 * i + 1]), 0, 0, false, -1, 16);
            var circular = ShapeMetrics.Measure(fixture);
            var ellipsePoints = circle.Select((value, i) => i % 2 == 0 ? value * 1.2f : value).ToArray();
            var elliptical = ShapeMetrics.Measure(fixture with { Contour = ellipsePoints });
            Require(circular.IsRound, "不均匀采样的完美圆被错误拒绝。");
            Require(!elliptical.IsRound, "20%横向拉长的椭圆被错误接受。");
            results.Add(new("round-metric-positive-and-negative-controls", true, new { circular, elliptical }));
        });

        Check("default-long-idle", () =>
        {
            using var session = new MaterialSession(library, new MaterialControls().Configure());
            var tail = new List<(double X, double Y, ShapeMetrics Shape)>();
            for (var i = 0; i < 36000; i++)
            {
                var state = session.Step(); var shape = ShapeMetrics.Measure(state);
                Require(shape.LowestWorldY >= session.Configuration.GroundHeight - .002, "长期运行轮廓穿地。");
                if (i >= 35400)
                {
                    Require(state.IsGrounded && state.MaximumSpeed < .03, "长期运行未接地停稳。");
                    tail.Add((state.CoreX, state.CoreY, shape));
                }
            }
            var drift = new[] { tail.Max(p => p.X) - tail.Min(p => p.X), tail.Max(p => p.Y) - tail.Min(p => p.Y),
                tail.Max(p => p.Shape.Width) - tail.Min(p => p.Shape.Width), tail.Max(p => p.Shape.Height) - tail.Min(p => p.Shape.Height) }.Max();
            Require(drift < .003, "长期运行持续漂移。");
            results.Add(new("default-long-idle", true, new { frames = session.Frames, drift }));
        });

        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            schema = 1, nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))),
            passed = results.Count(r => r.Passed), failed = results.Count(r => !r.Passed), results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return results.All(r => r.Passed) ? 0 : 1;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record RecoveryTrace(double ReleaseSpread, double RecoverySeconds, IReadOnlyList<double> Spread);

    private static RecoveryTrace Recover(string library, MaterialControls controls, bool requireRound)
    {
        using var session = new MaterialSession(library, controls.Configure(), height: 30);
        session.Step();
        for (var i = 0; i < 60; i++)
        {
            var state = session.State!;
            var pulled = session.Step(drag: true, dragX: state.CoreX,
                dragY: state.CoreY + session.Configuration.RadialRestLength * 2.5);
            Require(!pulled.IsGrounded && pulled.GroundContactCount == 0, "拖动形变期间发生接触。");
        }
        var releaseSpread = ShapeMetrics.Measure(session.State!).RadialSpread;
        Require(releaseSpread >= .10, $"松开前没有形成有效形变：{releaseSpread:F5}。");
        var consecutiveRoundFrames = 0; var recoveredAt = -1; var spread = new List<double>();
        for (var i = 0; i < 108; i++)
        {
            var state = session.Step(); var shape = ShapeMetrics.Measure(state); spread.Add(shape.RadialSpread);
            Require(!state.IsGrounded && state.GroundContactCount == 0 &&
                shape.LowestWorldY > session.Configuration.GroundHeight + .05, "恢复期间发生接触。");
            consecutiveRoundFrames = shape.IsRound ? consecutiveRoundFrames + 1 : 0;
            if (consecutiveRoundFrames >= 18 && recoveredAt < 0) recoveredAt = i - 17;
        }
        if (requireRound) Require(recoveredAt is >= 0 and <= 90, "松开后1.5秒内未恢复圆形并持续0.3秒。");
        return new(releaseSpread, recoveredAt / 60d, spread);
    }
}
