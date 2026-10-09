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
            var library = Path.GetFullPath(args[0]);
            if (args.Length == 3 && args[1] == "--verify") return Verification.Run(library, args[2]);
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
