using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Dango.PhysicsLab;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Dango.slnx"))) root = root.Parent;
        if (root is null) return 2;
        var module = Path.Combine(root.FullName, "artifacts", "physics-lab", "native", "Release", "dango_physics_lab.dll");
        if (!File.Exists(module)) return 3;
        var session = Path.Combine(root.FullName, "artifacts", "physics-lab", "sessions", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "manifest.json"), JsonSerializer.Serialize(new
        {
            kind = "isolated-physics-lab", gait = false, restSleep = false,
            nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
            fixedDt = LabWindow.FixedDt, measurementDipPerWorld = 72, displayDipPerWorld = 120,
            floorBounds = new[] { -256, 256 }, createdUtc = DateTime.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (args.Contains("--experiments"))
        {
            try { LabExperiments.Run(module, session); return 0; }
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
        var window = new LabWindow(module, session);
        if (args.Contains("--smoke")) window.Loaded += async (_, _) => await LabWindowSmoke.Run(window, session);
        return app.Run(window);
    }
}
