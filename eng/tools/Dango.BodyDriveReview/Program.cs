using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.BodyDriveReview;

internal static class Program
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("C0" or "C1")) return 2;
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dango.slnx"))) repository = repository.Parent;
        if (repository is null) return 3;
        var root = Path.Combine(repository.FullName, "artifacts", "body-drive");
        var role = args[0];
        var directory = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(directory) || File.Exists(directory)) return 4;
        for (var parent = new DirectoryInfo(directory).Parent; parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) return 4;
        var module = Path.Combine(root, "d1", "native", "Release",
            role == "C0" ? "dango_body_drive_core_review.dll" : "dango_body_drive_mass_review.dll");
        using var frozen = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "d1", "d0-frozen-manifest.json")));
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module)));
        if (!string.Equals(hash, frozen.RootElement.GetProperty("moduleHashes").GetProperty(role).GetString(), StringComparison.OrdinalIgnoreCase)) return 5;
        Directory.CreateDirectory(directory);
        var settings = Path.Combine(directory, "fictional-settings.json");
        new ControlCenterWindowSettingsStore().Save(settings, ControlCenterWindowSettings.CreateDefault() with
        {
            IntroSeen = true, DisplayMode = DesktopDisplayMode.Window, InteractionEnabled = true,
            ShowDebugSkeleton = false, KeyboardControl = new(true, KeyboardControlScheme.Wasd),
        });
        object? host = null;
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(Path.Combine(directory, "failure.json"), JsonSerializer.Serialize(new { kind = e.Exception.GetType().Name }));
            e.Handled = true;
            app.Shutdown(10);
        };
        app.Startup += (_, _) =>
        {
            var hostType = typeof(StackedDangoSceneRuntime).Assembly.GetType("Dango.Desktop.AppShell.DangoApplicationHost", true)!;
            var constructor = hostType.GetConstructors(Fields).Single();
            var parameters = constructor.GetParameters();
            var values = parameters.Select(p => p.DefaultValue).ToArray();
            void Set(string name, object value) => values[Array.FindIndex(parameters, p => p.Name == name)] = value;
            Set("settingsFile", settings); Set("nativeLibraryPath", module); Set("isolatedPorchReview", true);
            host = constructor.Invoke(values);
            hostType.GetMethod("Start")!.Invoke(host, null);
            hostType.GetMethod("ShowControlCenter")!.Invoke(host, null);
            var window = (Window)hostType.GetField("controlCenterWindow", Fields)!.GetValue(host)!;
            var home = (ProductHomeView)window.GetType().GetField("ProductHome", Fields)!.GetValue(window)!;
            var runtime = (StackedDangoSceneRuntime)hostType.GetField("stackedRuntime", Fields)!.GetValue(host)!;
            var gaitEnabled = true;
            void UpdateTitle() => window.Title = $"Dango · {role} · " +
                (gaitEnabled ? "步态叠加开启（F2 对照）" : "原物理轮廓（F2 恢复步态）");
            window.PreviewKeyDown += (_, key) =>
            {
                if (key.Key != Key.F2 || Keyboard.Modifiers != ModifierKeys.None) return;
                key.Handled = true;
                if (key.IsRepeat) return;
                gaitEnabled = !gaitEnabled;
                typeof(StackedDangoSceneRuntime).GetProperty("GaitEnabled", Fields)!.SetValue(runtime, gaitEnabled);
                UpdateTitle();
            };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            var attempts = 0;
            timer.Tick += (_, _) =>
            {
                if (++attempts > 100) { timer.Stop(); app.Shutdown(11); return; }
                if (!window.IsLoaded || !window.IsVisible) return;
                if (attempts == 1 || !home.IsInPorch)
                    typeof(ProductHomeView).GetMethod("EnterPorch", Fields)!.Invoke(home, null);
                if (runtime.LastPhysicsFrame is not { Succeeded: true } || !home.IsInPorch) return;
                timer.Stop();
                UpdateTitle();
                var work = SystemParameters.WorkArea;
                window.Width = Math.Max(window.MinWidth, Math.Min(720, work.Width / 2 - 16));
                window.Height = Math.Max(window.MinHeight, Math.Min(800, work.Height - 40));
                window.Left = work.Left + (role == "C0" ? 8 : work.Width / 2 + 4);
                window.Top = work.Top + 20;
                home.RestoreKeyboardFocus();
                File.WriteAllText(Path.Combine(directory, "started.json"), JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId, role, product = true, windowVisible = window.IsVisible,
                    porch = home.IsInPorch, gait = gaitEnabled, nativeSha256 = hash, state = runtime.InitializationState.ToString(),
                    physicalBodies = runtime.LastPhysicsFrame.Bodies.Count,
                }));
            };
            timer.Start();
        };
        app.Exit += (_, _) =>
        {
            (host as IDisposable)?.Dispose();
            File.WriteAllText(Path.Combine(directory, "exit.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, role }));
        };
        return app.Run();
    }
}
