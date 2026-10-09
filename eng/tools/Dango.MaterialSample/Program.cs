using System.IO;
using System.Windows;

namespace Dango.MaterialSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length is not 1 and not 3) return 2;
            if (args.Length == 3 && args[1] == "--measure-idle-probe") return IdleDiagnostics.MeasureProbe(args[0], args[2]);
            var library = Path.GetFullPath(args[0]);
            if (args.Length == 3 && args[1] == "--verify") return Verification.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--verify-drag") return DragVerification.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--diagnose-idle") return IdleDiagnostics.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--probe-drag") return DragVerification.Run(library, args[2], minimal: true);
            if (args.Length == 3 && args[1] == "--calibrate") return Calibration.Run(library, args[2]);
            if (args.Length == 3 && args[1] == "--calibrate-stability") return Calibration.Stability(library, args[2]);
            if (args.Length == 3 && args[1] == "--calibrate-forces") return Calibration.Stability(library, args[2], "balance");
            if (args.Length == 3 && args[1] == "--calibrate-damping") return Calibration.Stability(library, args[2], "damping");
            var app = new Application();
            using var window = new MaterialWindow(library);
            if (args.Length == 3 && args[1] == "--snapshot")
            {
                window.SaveSnapshot(args[2]);
                return 0;
            }
            if (args.Length != 1) return 2;
            return app.Run(window);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (args.Length == 1) MessageBox.Show(error.Message, "团子物理小样启动失败");
            return 1;
        }
    }
}
