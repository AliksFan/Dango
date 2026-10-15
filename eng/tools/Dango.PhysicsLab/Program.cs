using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Dango.PhysicsLab;

internal sealed record LabModules(string Discrete, string Envelope, string Regions, string RegionsEnvelope, string Arcs);

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Dango.slnx"))) root = root.Parent;
        if (root is null) return 2;
        var module = Path.Combine(root.FullName, "artifacts", "physics-lab", "native", "Release", "dango_physics_lab.dll");
        var envelopeModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "envelope-native", "Release", "dango_physics_lab_envelope.dll");
        var regionModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "arc-native", "Release", "dango_physics_lab_half.dll");
        var regionEnvelopeModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "arc-native", "Release", "dango_physics_lab_half_envelope.dll");
        var arcModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "arc-native", "Release", "dango_physics_lab_arcs.dll");
        var beltModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "belt-native", "Release", "dango_physics_lab_belt.dll");
        var useBelt = args.Contains("--belt") || args.Contains("--belt-experiments");
        if (useBelt && (args.Any(a => a is not ("--belt" or "--belt-experiments" or "--smoke")) ||
            args.Contains("--belt-experiments") && args.Contains("--smoke")))
        { Console.Error.WriteLine("闭合带请单独使用 --belt [--smoke] 或 --belt-experiments；本轮仅原始显示。"); return 7; }
        if (args.Contains("--arcs") || args.Contains("--arc-experiments"))
        { Console.Error.WriteLine("弹性弧段方案已废弃；请用 --belt 或 --belt-experiments。历史代码与结果已保留。"); return 8; }
        var useArcs = args.Contains("--arcs");
        var useRegions = args.Contains("--regions") || useArcs || useBelt;
        if (args.Contains("--region-point")) { Console.Error.WriteLine("单节点实验已取消；请用 --regions 或 --belt。 "); return 7; }
        var useSmoothing = args.Contains("--smooth") && !useBelt;
        if (!File.Exists(module)) return 3;
        var useEnvelope = args.Contains("--envelope");
        if ((useEnvelope || args.Contains("--ground-experiments")) && !File.Exists(envelopeModule)) return 4;
        if ((useRegions || args.Contains("--arc-experiments")) && !File.Exists(regionModule)) return 5;
        if (useRegions && useEnvelope && !File.Exists(regionEnvelopeModule)) return 6;
        if ((useArcs || args.Contains("--arc-experiments")) && !File.Exists(arcModule)) return 8;
        if (useBelt && !File.Exists(beltModule)) return 9;
        var session = Path.Combine(root.FullName, "artifacts", "physics-lab", "sessions", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "manifest.json"), JsonSerializer.Serialize(new
        {
            kind = "isolated-physics-lab", gait = false, restSleep = false,
            nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
            envelopeNativeSha256 = File.Exists(envelopeModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(envelopeModule))) : null,
            regionNativeSha256 = File.Exists(regionModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(regionModule))) : null,
            regionEnvelopeNativeSha256 = File.Exists(regionEnvelopeModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(regionEnvelopeModule))) : null,
            arcNativeSha256 = File.Exists(arcModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(arcModule))) : null,
            beltNativeSha256 = File.Exists(beltModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(beltModule))) : null,
            initialGroundModel = useBelt ? "continuous-belt" : useEnvelope ? "continuous-envelope" : "discrete-nodes",
            initialDrive = useRegions ? "direction-half-region" : "core", regionDenominator = 2,
            initialDisplaySmoothing = useSmoothing, displaySmoothingTauSeconds = LabDisplaySmoothing.TimeConstantSeconds,
            fixedDt = LabWindow.FixedDt, measurementDipPerWorld = 72, displayDipPerWorld = 120,
            floorBounds = new[] { -256, 256 }, createdUtc = DateTime.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (args.Contains("--experiments") || args.Contains("--ground-experiments") || args.Contains("--belt-experiments"))
        {
            try
            {
                if (args.Contains("--belt-experiments")) LabBeltExperiments.Run(module, regionModule, beltModule, session);
                else if (args.Contains("--ground-experiments")) LabGroundExperiments.Run(module, envelopeModule, session);
                else LabExperiments.Run(module, session);
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(session, "failure.json"), JsonSerializer.Serialize(new { error = error.ToString() }));
                return 10;
            }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(Path.Combine(session, "failure.json"), JsonSerializer.Serialize(new { error = e.Exception.ToString() }));
            e.Handled = true;
            app.Shutdown(10);
        };
        if (useBelt)
        {
            var beltWindow = new LabBeltWindow(regionModule, beltModule, session);
            if (args.Contains("--smoke")) beltWindow.Loaded += async (_, _) => await beltWindow.Smoke();
            return app.Run(beltWindow);
        }
        var window = new LabWindow(new(module, envelopeModule, regionModule, regionEnvelopeModule, arcModule), session, useEnvelope, useRegions, useSmoothing, useArcs);
        if (args.Contains("--smoke")) window.Loaded += async (_, _) => await LabWindowSmoke.Run(window, session);
        return app.Run(window);
    }
}
