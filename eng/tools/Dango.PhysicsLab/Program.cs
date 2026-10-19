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
        var calibratedBeltModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "belt-calibration-native", "Release", "dango_physics_lab_belt.dll");
        var originalJumpBeltModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "belt-calibration-v2-native", "Release", "dango_physics_lab_belt.dll");
        var guardedBeltModule = Path.Combine(root.FullName, "artifacts", "physics-lab", "belt-rear-pressure-native", "Release", "dango_physics_lab_belt.dll");
        var beltBatches = new[] { "--belt-experiments", "--belt-calibration", "--belt-candidates", "--belt-refinement", "--belt-dynamics", "--belt-guard-experiments", "--belt-joint-validation" };
        var jointValidation = args.Contains("--belt-joint-validation");
        var useBelt = args.Contains("--belt") || args.Any(beltBatches.Contains);
        if (useBelt && (args.Any(a => a is not ("--belt" or "--belt-experiments" or "--belt-calibration" or "--belt-candidates" or "--belt-refinement" or "--belt-dynamics" or "--belt-guard-experiments" or "--belt-joint-validation" or "--smoke" or "--saved-appearance")) ||
            args.Count(beltBatches.Contains) > 1 || args.Any(beltBatches.Contains) && args.Contains("--smoke")))
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
        if ((args.Contains("--belt-calibration") || args.Contains("--belt-candidates") || args.Contains("--belt-refinement")) && !File.Exists(calibratedBeltModule)) return 9;
        if (args.Contains("--belt-dynamics") && !File.Exists(originalJumpBeltModule)) return 9;
        if ((args.Contains("--belt-guard-experiments") || jointValidation || useBelt && !args.Any(beltBatches.Contains)) && !File.Exists(guardedBeltModule))
        { Console.Error.WriteLine("缺少形状保护实验组件，请重新生成本轮实验应用后启动。"); return 9; }
        var session = Path.Combine(root.FullName, "artifacts", "physics-lab", "sessions",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "manifest.json"), JsonSerializer.Serialize(new
        {
            kind = "isolated-physics-lab", gait = useBelt && (!args.Any(beltBatches.Contains) || jointValidation),
            gaitMode = useBelt && (!args.Any(beltBatches.Contains) || jointValidation) ? "original-grounded-presentation-preview" : "disabled",
            restSleep = false,
            nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
            envelopeNativeSha256 = File.Exists(envelopeModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(envelopeModule))) : null,
            regionNativeSha256 = File.Exists(regionModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(regionModule))) : null,
            regionEnvelopeNativeSha256 = File.Exists(regionEnvelopeModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(regionEnvelopeModule))) : null,
            arcNativeSha256 = File.Exists(arcModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(arcModule))) : null,
            beltNativeSha256 = File.Exists(beltModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(beltModule))) : null,
            calibratedBeltNativeSha256 = File.Exists(calibratedBeltModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(calibratedBeltModule))) : null,
            originalJumpBeltNativeSha256 = File.Exists(originalJumpBeltModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalJumpBeltModule))) : null,
            guardedBeltNativeSha256 = File.Exists(guardedBeltModule) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(guardedBeltModule))) : null,
            initialGroundModel = useBelt ? "continuous-belt" : useEnvelope ? "continuous-envelope" : "discrete-nodes",
            initialDrive = useRegions ? "direction-half-region" : "core", regionDenominator = 2,
            initialDisplaySmoothing = useSmoothing, displaySmoothingTauSeconds = LabDisplaySmoothing.TimeConstantSeconds,
            fixedDt = LabWindow.FixedDt, measurementDipPerWorld = 72, displayDipPerWorld = 120,
            floorBounds = new[] { -256, 256 }, createdUtc = DateTime.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!jointValidation && (args.Contains("--experiments") || args.Contains("--ground-experiments") || args.Any(beltBatches.Contains)))
        {
            try
            {
                if (args.Contains("--belt-guard-experiments"))
                {
                    if (!LabBeltGuardExperiments.Run(guardedBeltModule, session))
                        throw new InvalidOperationException("形状保护极值实验未通过，详见本次实验记录。");
                }
                else if (args.Contains("--belt-dynamics")) LabBeltDynamics.Run(regionModule, originalJumpBeltModule, session);
                else if (args.Contains("--belt-candidates") || args.Contains("--belt-refinement"))
                {
                    var reference = Directory.EnumerateFiles(Path.GetDirectoryName(session)!, "calibration-results.json", SearchOption.AllDirectories)
                        .OrderByDescending(file => Directory.GetParent(file)!.Name).FirstOrDefault()
                        ?? throw new InvalidOperationException("请先运行 --belt-calibration 生成同模块参考。");
                    if (args.Contains("--belt-refinement")) LabBeltCalibration.RunRefinement(regionModule, beltModule, calibratedBeltModule, session, reference);
                    else LabBeltCalibration.RunCandidates(regionModule, beltModule, calibratedBeltModule, session, reference);
                }
                else if (args.Contains("--belt-calibration")) LabBeltCalibration.Run(regionModule, beltModule, calibratedBeltModule, session);
                else if (args.Contains("--belt-experiments")) LabBeltExperiments.Run(module, regionModule, beltModule, session);
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
        if (jointValidation)
        {
            var jointWindow = new LabBeltJointValidationWindow(guardedBeltModule, session, LabAppearanceFixture.Create(session));
            jointWindow.Loaded += (_, _) => jointWindow.Dispatcher.BeginInvoke(new Action(jointWindow.Run));
            return app.Run(jointWindow);
        }
        if (useBelt)
        {
            var useTestAppearance = args.Contains("--smoke") || !args.Contains("--saved-appearance");
            var appearanceSettingsPath = useTestAppearance ? LabAppearanceFixture.Create(session) :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "settings.json");
            var beltWindow = new LabBeltWindow(regionModule, beltModule, guardedBeltModule, session, appearanceSettingsPath,
                useTestAppearance ? "内置测试搭配 · 不代表日常佩戴" : "当前已保存外观 · 只读快照");
            if (args.Contains("--smoke")) beltWindow.Loaded += async (_, _) => await beltWindow.Smoke();
            return app.Run(beltWindow);
        }
        var window = new LabWindow(new(module, envelopeModule, regionModule, regionEnvelopeModule, arcModule), session, useEnvelope, useRegions, useSmoothing, useArcs);
        if (args.Contains("--smoke")) window.Loaded += async (_, _) => await LabWindowSmoke.Run(window, session);
        return app.Run(window);
    }
}
