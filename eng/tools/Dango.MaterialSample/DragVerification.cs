using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

internal static class DragVerification
{
    internal static int Run(string library, string output, bool minimal = false)
    {
        var results = new List<object>();
        var failures = 0;
        // Pure rendering fixtures, not native trajectories. The flattened circle
        // includes legal low-side points that the old absolute size check rejected
        // at 120%; a real canonical foot must still be rejected at every size.
        if (!minimal) foreach (var bodySize in new[] { .8, 1d, 1.2 })
        {
            try
            {
                var contour = Enumerable.Range(0, 16).SelectMany(i =>
                {
                    var angle = 2 * Math.PI * i / 16;
                    return new[] { (float)(.82 * Math.Cos(angle)), (float)Math.Max(-.78, .82 * Math.Sin(angle)) };
                }).ToArray();
                var fixture = new DangoBodyState(new(1), contour, 0, 0, 1.6f, 0, 0, 0, 0, false, -1, 16);
                var standard = SampleGeometry.Create(fixture).Bounds;
                var scaled = fixture with { Contour = contour.Select(value => (float)(value * bodySize)).ToArray() };
                var bounds = SampleGeometry.Create(scaled, bodySize: bodySize).Bounds;
                if (Math.Abs(bounds.Width / standard.Width - bodySize) > .0001 ||
                    Math.Abs(bounds.Height / standard.Height - bodySize) > .0001)
                    throw new InvalidOperationException("尺寸规范化改变了实际绘制大小。");
                var foot = contour.ToArray(); foot[24] = -.72f; foot[25] = -.98f;
                var rejected = false;
                try
                {
                    _ = SampleGeometry.Create(fixture with { Contour = foot.Select(value => (float)(value * bodySize)).ToArray() }, bodySize: bodySize);
                }
                catch (InvalidOperationException error) when (error.Message.Contains("FootLikeSpike", StringComparison.Ordinal)) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("真实 canonical 尖脚负例被接受。");
                results.Add(new { name = "synthetic-render-size-positive-and-foot-negative", bodySize, passed = true,
                    widthRatio = bounds.Width / standard.Width, heightRatio = bounds.Height / standard.Height, footRejected = rejected });
            }
            catch (Exception error) { failures++; results.Add(new { name = "synthetic-render-size-positive-and-foot-negative", bodySize, passed = false, error = error.Message }); }
        }
        if (!minimal) foreach (var scaleY in new[] { 105d, 105d * 58 / 72 })
        {
            try
            {
                var coordinates = new SceneCoordinates();
                const double height = 324, width = 500, scaleX = 105, ground = -.72;
                var pointer = new Point(180, 80);
                var switchY = (height - 36 + ground * scaleY - height * .38) / scaleY;
                foreach (var coreY in new[] { switchY, 1.9 })
                {
                    var a = coordinates.CameraOrigin(height, scaleY, ground, coreY - .0001);
                    var b = coordinates.CameraOrigin(height, scaleY, ground, coreY + .0001);
                    if (Math.Abs(b - a) > .03) throw new InvalidOperationException("镜头阈值造成显示跳变。");
                }
                var start = coordinates.WorldTarget(pointer, width, height, scaleX, scaleY, ground, 0);
                coordinates.BeginDrag(height, scaleY, ground, 0);
                foreach (var coreY in new[] { -.5, 1.9, 1.901, 8d })
                {
                    var samePointer = coordinates.WorldTarget(pointer, width, height, scaleX, scaleY, ground, coreY);
                    if ((samePointer - start).Length > 1e-9) throw new InvalidOperationException("拖动目标受身体/镜头运动反馈。");
                }
                var moved = coordinates.WorldTarget(pointer + new Vector(21, -scaleY / 5), width, height, scaleX, scaleY, ground, 8);
                if (Math.Abs(moved.X - start.X - .2) > 1e-9 || Math.Abs(moved.Y - start.Y - .2) > 1e-9)
                    throw new InvalidOperationException("鼠标移动与世界目标比例不一致。");
                coordinates.EndDrag();
                if (coordinates.IsDragging) throw new InvalidOperationException("停止拖动后保留了镜头捕获。");
                results.Add(new { name = "camera-and-pointer-coordinates", scaleY, passed = true });
            }
            catch (Exception error) { failures++; results.Add(new { name = "camera-and-pointer-coordinates", scaleY, passed = false, error = error.Message }); }
        }
        foreach (var profile in minimal ? new[] { "ground-swing" } : new[] { "changing", "horizontal", "ground-swing" })
        foreach (var size in minimal ? new[] { 100d } : new[] { 80d, 100d, 120d })
        foreach (var softness in minimal ? new[] { 50d } : new[] { -1d, 0d, 50d, 100d })
        {
            if (softness < 0 && size != 100) continue;
            var config = softness < 0 ? DangoPhysicsDefaults.Current : new MaterialControls(size, softness).Configure();
            using var session = new MaterialSession(library, config, profile == "changing" ? 5d / 3 : 0);
            var clock = Stopwatch.StartNew();
            double targetX = 0, targetY = 0, peakSpeed = 0, maximumCanonicalRadius = 0;
            var active = false;
            var phase = "settle";
            try
            {
                for (var i = 0; i < 6000; i++)
                {
                    phase = i < 600 ? "settle" : "swing";
                    active = i >= 600 && i % 600 < 480;
                    (targetX, targetY) = profile switch
                    {
                        "changing" => (-3.25 + 3.75 * Math.Sin(i * .043), 10d / 3 - 25d / 6 * Math.Cos(i * .071)),
                        "horizontal" => (i / 6 % 2 == 0 ? -2d : 2d, 1.5),
                        _ => (2.4 * Math.Sin(i * .12), .4 + 1.5 * Math.Cos(i * .17)),
                    };
                    var state = session.Step(drag: active, dragX: targetX, dragY: targetY);
                    peakSpeed = Math.Max(peakSpeed, state.MaximumSpeed);
                    maximumCanonicalRadius = Math.Max(maximumCanonicalRadius, RenderNative(session, state));
                }
                phase = "release";
                for (var i = 0; i < 600; i++) maximumCanonicalRadius = Math.Max(maximumCanonicalRadius, RenderNative(session, session.Step()));
                var startX = session.State!.CoreX;
                phase = "renewed-control";
                for (var i = 0; i < 60; i++) maximumCanonicalRadius = Math.Max(maximumCanonicalRadius, RenderNative(session, session.Step(horizontal: 1)));
                var moved = session.State!.CoreX - startX;
                if (moved < .04) throw new InvalidOperationException($"甩动后没有继续响应方向力：{moved:F6}");
                results.Add(new { profile, size, softness, passed = true, frames = session.Frames, peakSpeed, maximumCanonicalRadius, moved, elapsedSeconds = clock.Elapsed.TotalSeconds });
                Console.WriteLine($"{profile}/size={size}/softness={softness}: PASS");
            }
            catch (Exception error)
            {
                failures++;
                results.Add(new { profile, size, softness, passed = false, phase, frames = session.Frames,
                    targetX, targetY, active, peakSpeed, maximumCanonicalRadius, error = error.Message, lastFrame = session.State });
                Console.WriteLine($"{profile}/size={size}/softness={softness}: FAIL frame={session.Frames}: {error.Message}");
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { kind = "changing-drag-regression",
                nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))), passed = results.Count - failures, failures, results },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        return failures == 0 ? 0 : 1;
    }

    private static double RenderNative(MaterialSession session, DangoBodyState state)
    {
        var maximumRadius = Enumerable.Range(0, state.PointCount).Max(i =>
            Math.Sqrt(Math.Pow(state.Contour[2 * i], 2) + Math.Pow(state.Contour[2 * i + 1], 2)));
        var limit = session.Configuration.RadialRestLength * session.Configuration.RadialMaximumRatio + session.Configuration.NodeRadius;
        // Independent of presentation normalization: native output may exceed the
        // distance limit only by its 1e-5 solve tolerance plus float roundoff.
        if (maximumRadius > limit + .00002)
            throw new InvalidOperationException($"真实绘制轮廓越过径向硬上限：{maximumRadius:F7} > {limit:F7}");
        _ = SampleGeometry.Create(state, bodySize: session.BodySize);
        return maximumRadius / session.BodySize;
    }
}
