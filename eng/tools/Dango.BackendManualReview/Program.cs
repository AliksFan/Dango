using System.IO;
using System.Windows;
using Dango.Server.Hosting;

namespace Dango.BackendManualReview;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var binaryDirectory = ManualReviewTarget.ParseArguments(args);
            var root = FormalDeploymentPaths.ProtectedRoot();
            if (!File.Exists(Path.Combine(root, "src", "Dango.Server", "Dango.Server.csproj")))
                throw new ManualReviewRejected("manual_review_source_root_required");
            var target = ManualReviewTarget.Load(true, root, binaryDirectory);
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            application.Run(new ManualReviewWindow(new(target)));
        }
        catch (Exception error)
        {
            MessageBox.Show(SafeError(error), "Dango B5C 验收准备停止", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static string SafeError(Exception error)
    {
        var code = error is ManualReviewRejected rejected ? rejected.Code : error.Message;
        return code.StartsWith("manual_", StringComparison.Ordinal) && code.Length <= 160
            && code.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')
            ? code : "manual_review_operation_failed";
    }
}
