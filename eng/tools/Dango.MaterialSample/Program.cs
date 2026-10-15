using System.IO;
using System.Security.Cryptography;
using System.Windows;

namespace Dango.MaterialSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[1] == "--corners")
                return new Application().Run(new CornerWindow(Path.GetFullPath(args[0])));
            if (args.Contains("--verify-landing-build-parallel-long-trial"))
                return args.Length == 7 && args[1] == "--verify-landing-build-parallel-long-trial" &&
                    int.TryParse(args[6], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var longIndex)
                    ? LandingBuildParallelLongTrial.Run(args[0], args[2], args[3], args[4], args[5], longIndex) : 2;
            if (args.Contains("--verify-landing-build-drift-parallel-contract"))
                return args.Length == 3 && args[0] == "--verify-landing-build-drift-parallel-contract"
                    ? LandingBuildDriftDiagnostic.VerifyContract(args[1], args[2]) : 2;
            if (args.Contains("--diagnose-landing-build-drift-parallel"))
                return args.Length == 6 && args[1] == "--diagnose-landing-build-drift-parallel"
                    ? LandingBuildDriftDiagnostic.Run(args[0], args[2], args[3], args[4], args[5]) : 2;
            if (args.Contains("--verify-landing-build-touching-receipt-long-contract"))
                return args.Length == 2 && args[0] == "--verify-landing-build-touching-receipt-long-contract"
                    ? LandingBuildTouchingReceiptLongVerification.VerifyContract(args[1]) : 2;
            if (args.Contains("--verify-landing-build-touching-receipt-long"))
                return args.Length == 6 && args[1] == "--verify-landing-build-touching-receipt-long"
                    ? LandingBuildTouchingReceiptLongVerification.Run(args[0], args[2], args[3], args[4], args[5]) : 2;
            if (args.Contains("--verify-landing-build-touching-receipt-old-error-gate"))
                return args.Length == 1 ? LandingBuildTouchingReceiptVerification.VerifyOldErrorGate() : 2;
            if (args.Contains("--verify-landing-build-touching-receipt-former"))
                return args.Length == 7 && args[1] == "--verify-landing-build-touching-receipt-former"
                    ? LandingBuildTouchingReceiptVerification.RunFormerFailures(args[0], args[2], args[3], args[4], args[5], args[6]) : 2;
            if (args.Contains("--verify-landing-build-touching-receipt-landing"))
                return args.Length == 5 && args[1] == "--verify-landing-build-touching-receipt-landing"
                    ? LandingBuildTouchingReceiptVerification.RunLanding(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-touching-receipt-motion"))
                return args.Length == 5 && args[1] == "--verify-landing-build-touching-receipt-motion"
                    ? LandingBuildTouchingReceiptVerification.RunMotion(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-singleton-mode-old-error-gate"))
                return args.Length == 1 ? LandingBuildSingletonModeVerification.VerifyOldErrorGate() : 2;
            if (args.Contains("--verify-landing-build-singleton-mode-former"))
                return args.Length == 6 && args[1] == "--verify-landing-build-singleton-mode-former"
                    ? LandingBuildSingletonModeVerification.RunFormerFailures(args[0], args[2], args[3], args[4], args[5]) : 2;
            if (args.Contains("--verify-landing-build-singleton-mode-landing"))
                return args.Length == 5 && args[1] == "--verify-landing-build-singleton-mode-landing"
                    ? LandingBuildSingletonModeVerification.RunLanding(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-singleton-mode-motion"))
                return args.Length == 5 && args[1] == "--verify-landing-build-singleton-mode-motion"
                    ? LandingBuildSingletonModeVerification.RunMotion(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-endpoint-selection-old-error-gate"))
                return args.Length == 1 ? LandingBuildEndpointSelectionVerification.VerifyOldErrorGate() : 2;
            if (args.Contains("--verify-landing-build-endpoint-selection-former"))
                return args.Length == 5 && args[1] == "--verify-landing-build-endpoint-selection-former"
                    ? LandingBuildEndpointSelectionVerification.RunFormerFailures(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-endpoint-selection-landing"))
                return args.Length == 5 && args[1] == "--verify-landing-build-endpoint-selection-landing"
                    ? LandingBuildEndpointSelectionVerification.RunLanding(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-endpoint-selection-motion"))
                return args.Length == 5 && args[1] == "--verify-landing-build-endpoint-selection-motion"
                    ? LandingBuildEndpointSelectionVerification.RunMotion(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-receipt-history-old-error-gate"))
                return args.Length == 1 ? LandingBuildReceiptHistoryVerification.VerifyOldErrorGate() : 2;
            if (args.Contains("--verify-landing-build-receipt-history-old-baseline"))
                return args.Length == 3 && args[1] == "--verify-landing-build-receipt-history-old-baseline"
                    ? LandingBuildReceiptHistoryVerification.RunFreshOldBaseline(args[0], args[2]) : 2;
            if (args.Contains("--verify-landing-build-receipt-history-former"))
                return args.Length == 4 && args[1] == "--verify-landing-build-receipt-history-former"
                    ? LandingBuildReceiptHistoryVerification.RunFormerFailure(args[0], args[2], args[3]) : 2;
            if (args.Contains("--verify-landing-build-receipt-history-landing"))
                return args.Length == 5 && args[1] == "--verify-landing-build-receipt-history-landing"
                    ? LandingBuildReceiptHistoryVerification.RunLanding(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-receipt-history-motion"))
                return args.Length == 5 && args[1] == "--verify-landing-build-receipt-history-motion"
                    ? LandingBuildReceiptHistoryVerification.RunMotion(args[0], args[2], args[3], args[4]) : 2;
            if (args.Contains("--verify-landing-build-size-damping-motion"))
                return args.Length == 6 && args[1] == "--verify-landing-build-size-damping-motion"
                    ? LandingBuildSizeDampingMotionVerification.Run(args[0], args[2], args[3], args[4], args[5]) : 2;
            if (args.Contains("--verify-landing-build-size-damping"))
                return args.Length == 6 && args[1] == "--verify-landing-build-size-damping"
                    ? LandingBuildRadialDampingVerification.RunSizeCompensated(args[0], args[2], args[3], args[4], args[5]) : 2;
            if (args.Contains("--verify-landing-build-radial-damping"))
                return args.Length == 6 && args[1] == "--verify-landing-build-radial-damping"
                    ? LandingBuildRadialDampingVerification.Run(args[0], args[2], args[3], args[4], args[5]) : 2;
            if (args.Any(a => a is "--preview-landing-build-recovery" or "--verify-landing-build-recovery-landing") &&
                !(args.Length == 5 && args[1] == "--preview-landing-build-recovery" ||
                  args.Length == 6 && args[1] == "--verify-landing-build-recovery-landing")) return 2;
            if (args.Length == 5 && args[1] == "--preview-landing-build-recovery")
                return LandingBuildRecoveryVerification.Preview(args[0], args[2], args[3], args[4]);
            if (args.Length == 6 && args[1] == "--verify-landing-build-recovery-landing")
                return LandingBuildRecoveryVerification.RunLanding(args[0], args[2], args[3], args[4], args[5]);
            if (args.Length == 6 && args[1] == "--verify-landing-build-stretch-motion")
                return LandingBuildStretchMatrixVerification.RunMotion(Path.GetFullPath(args[0]), args[2], args[3], args[4], args[5]);
            if (args.Length == 5 && args[1] == "--verify-landing-build-stretch-landing")
                return LandingBuildStretchMatrixVerification.RunLanding(Path.GetFullPath(args[0]), args[2], args[3], args[4]);
            if (args.Length == 6 && args[1] == "--preview-landing-build-stretch")
                return LandingBuildStretchPreviewVerification.Run(Path.GetFullPath(args[0]), args[2], args[3], args[4], args[5]);
            if (args.Length == 4 && args[1] == "--verify-landing-build-landing")
                return LandingBuildMatrixVerification.RunLanding(Path.GetFullPath(args[0]), args[2], args[3]);
            if (args.Length == 4 && args[1] == "--verify-landing-build-motion")
                return LandingBuildMotionMatrixVerification.Run(Path.GetFullPath(args[0]), args[2], args[3]);
            if (args.Length is not 1 and not 3 and not 5) return 2;
            if (args.Length == 3 && args[1] == "--measure-idle-probe") return IdleDiagnostics.MeasureProbe(args[0], args[2]);
            var library = Path.GetFullPath(args[0]);
            string? baselineLibrary = null;
            if (args.Length == 5)
            {
                if (args[3] != "--baseline-library") return 2;
                baselineLibrary = Path.GetFullPath(args[4]);
                return args[1] switch
                {
                    "--verify" => Verification.Run(library, args[2], baselineLibrary),
                    "--verify-drag" => DragVerification.Run(library, args[2], baselineLibrary: baselineLibrary),
                    "--diagnose-idle" => IdleDiagnostics.Run(library, args[2], baselineLibrary),
                    _ => 2,
                };
            }
            if (args.Length == 3 && args[1] == "--baseline-library")
                baselineLibrary = Path.GetFullPath(args[2]);
            if (args.Length == 3 && args[1] == "--verify") return Verification.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--verify-landing-build-mapping") return LandingBuildVerification.Calibrate(library, args[2], mappingOnly: true);
            if (args.Length == 3 && args[1] == "--calibrate-landing-build") return LandingBuildVerification.Calibrate(library, args[2]);
            if (args.Length == 3 && args[1] == "--verify-drag") return DragVerification.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--diagnose-idle") return IdleDiagnostics.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--diagnose-post-drag-idle") return PostDragIdleDiagnostics.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--probe-drag") return DragVerification.Run(library, args[2], minimal: true);
            if (args.Length == 3 && args[1] == "--calibrate") return Calibration.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--calibrate-stability") return Calibration.Stability(library, args[2]);
            if (args.Length == 3 && args[1] == "--calibrate-forces") return Calibration.Stability(library, args[2], "balance");
            if (args.Length == 3 && args[1] == "--calibrate-damping") return Calibration.Stability(library, args[2], "damping");
            if (args.Length == 3 && args[1] is not "--snapshot" and not "--baseline-library") return 2;
            var app = new Application();
            using var window = new MaterialWindow(library, baselineLibrary);
            if (args.Length == 3 && args[1] == "--snapshot")
            {
                window.SaveSnapshot(args[2]);
                return 0;
            }
            if (args.Length != 1 && baselineLibrary is null) return 2;
            return app.Run(window);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (args.Length == 1 || args.Length == 3 && args[1] == "--baseline-library")
                MessageBox.Show(error.Message, "团子物理小样启动失败");
            return 1;
        }
    }
}

internal sealed record MaterialLibrarySource(string FileName, string Sha256, string Selection);
internal sealed record MaterialLibrarySources(MaterialLibrarySource Candidate, MaterialLibrarySource Baseline, bool ExplicitBaseline);

internal static class MaterialLibraries
{
    internal static MaterialLibrarySources Describe(string library, string? baselineLibrary)
    {
        MaterialLibrarySource Source(string path, string selection) => new(Path.GetFileName(path),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), selection);
        return new(Source(library, "Primary CLI library; used for material candidate parameters."),
            Source(baselineLibrary ?? library, baselineLibrary is null
                ? "Primary CLI library fallback; legacy single-library default-parameter comparison."
                : "Explicit --baseline-library; used for original default-parameter comparison."),
            baselineLibrary is not null);
    }
}
