using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Dango.PhysicsLab;

internal sealed record LabModules(string Discrete, string Envelope, string Regions, string RegionsEnvelope);

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
        var regionModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "region-native", "Release", "dango_physics_lab_regions.dll");
        var regionEnvelopeModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "region-native", "Release", "dango_physics_lab_regions_envelope.dll");
        var useRegions = args.Contains("--regions") || args.Contains("--region-point");
        var useRegionPoint = args.Contains("--region-point");
        var useSmoothing = useRegions || args.Contains("--smooth");
        if (!File.Exists(module)) return 3;
        var useEnvelope = args.Contains("--envelope");
        if ((useEnvelope || args.Contains("--ground-experiments")) && !File.Exists(envelopeModule)) return 4;
        if ((useRegions || args.Contains("--region-experiments")) && !File.Exists(regionModule)) return 5;
        if (useRegions && useEnvelope && !File.Exists(regionEnvelopeModule)) return 6;
        var session = Path.Combine(root.FullName, "artifacts", "physics-lab", "sessions", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "manifest.json"), JsonSerializer.Serialize(new
        {
            kind = "isolated-physics-lab", gait = false, restSleep = false,
            nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
            envelopeNativeSha256 = File.Exists(envelopeModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(envelopeModule))) : null,
            regionNativeSha256 = File.Exists(regionModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(regionModule))) : null,
            regionEnvelopeNativeSha256 = File.Exists(regionEnvelopeModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(regionEnvelopeModule))) : null,
            initialGroundModel = useEnvelope ? "continuous-envelope" : "discrete-nodes",
            initialDrive = useRegions ? useRegionPoint ? "direction-point" : "direction-region" : "core",
            initialDisplaySmoothing = useSmoothing, displaySmoothingTauSeconds = LabDisplaySmoothing.TimeConstantSeconds,
            fixedDt = LabWindow.FixedDt, measurementDipPerWorld = 72, displayDipPerWorld = 120,
            floorBounds = new[] { -256, 256 }, createdUtc = DateTime.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (args.Contains("--experiments") || args.Contains("--ground-experiments") || args.Contains("--region-experiments"))
        {
            try
            {
                if (args.Contains("--region-experiments")) LabRegionExperiments.Run(module, regionModule, session);
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
        var window = new LabWindow(new(module, envelopeModule, regionModule, regionEnvelopeModule), session, useEnvelope, useRegions, useRegionPoint, useSmoothing);
        if (args.Contains("--smoke")) window.Loaded += async (_, _) => await LabWindowSmoke.Run(window, session);
        return app.Run(window);
    }
}
