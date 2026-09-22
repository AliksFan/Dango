using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.GaitSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        SampleSession? session = null;
        object? host = null;
        var exitCode = 0;
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Dango.slnx"))) root = root.Parent;
            if (root is null) return 3;
            session = SampleSession.Create(root.FullName, args[0], Path.Combine(AppContext.BaseDirectory, SampleSession.NativeFileName));
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) =>
            {
                WriteEvidence(session, "failure.json", new { kind = e.Exception.GetType().Name });
                e.Handled = true; app.Shutdown(10);
            };
            app.Startup += (_, _) =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = typeof(StackedDangoSceneRuntime).Assembly.GetType("Dango.Desktop.AppShell.DangoApplicationHost", true)!;
                host = Activator.CreateInstance(type, flags, null, session.HostArguments, null)!;
                type.GetMethod("Start")!.Invoke(host, null);
                var runtime = (StackedDangoSceneRuntime)type.GetField("stackedRuntime", flags)!.GetValue(host)!;
                if (runtime.InitializationState != SceneInitializationState.Ready) throw new InvalidOperationException("Sample initialization failed.");
                var snapshot = ((DesktopHostCoordinator)ReadField(host, "desktopHost")!).GetSnapshot();
                if (!snapshot.IsVisible || snapshot.Mode != "Window") throw new InvalidOperationException("Sample host is not visible in Window mode.");
                type.GetMethod("ShowControlCenter")!.Invoke(host, null);
                foreach (Window window in app.Windows) window.Title = "Dango 步态小样 · " + window.Title;
                File.WriteAllText(Path.Combine(session.DirectoryPath, "started.json"), JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId, product = true, mode = snapshot.Mode, visible = snapshot.IsVisible,
                    binaries = new[] { typeof(StackedDangoSceneRuntime).Assembly.Location, typeof(Dango.Physics.DangoPhysicsWorld).Assembly.Location,
                        typeof(Dango.Core.Coordinates.LogicalRect).Assembly.Location, session.ModulePath }.Select(BinaryIdentity),
                }));
            };
            exitCode = app.Run();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or TargetInvocationException)
        {
            if (session is not null) WriteEvidence(session, "failure.json", new { kind = exception.GetType().Name });
            exitCode = 4;
        }
        finally
        {
            try
            {
                (host as IDisposable)?.Dispose();
                if (session is not null) WriteEvidence(session, "exit.json", new
                {
                    pid = Environment.ProcessId,
                    hostDisposed = host is not null && ReadField(host, "disposed") is true,
                    trayReleased = host is not null && ReadField(host, "trayIcon") is null,
                    visibleWindows = System.Windows.Application.Current?.Windows.Cast<Window>().Count(w => w.IsVisible) ?? 0,
                    runtime = host is not null ? RuntimeExitState(ReadField(host, "stackedRuntime")!) : null,
                    geometrySaveStopped = host is not null && !((System.Windows.Threading.DispatcherTimer)ReadField(host, "geometrySaveTimer")!).IsEnabled,
                    windowHost = host is not null ? HostExitState(ReadField(host, "sceneWindow")!) : null,
                    wallpaperHost = host is not null ? HostExitState(ReadField(host, "workerWWindow")!) : null,
                });
            }
            catch (Exception exception)
            { exitCode = 10; if (session is not null) WriteEvidence(session, "failure.json", new { kind = exception.GetType().Name, phase = "dispose" }); }
            finally { session?.Dispose(); }
        }
        return exitCode;
    }

    private static object? ReadField(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);

    private static object RuntimeExitState(object runtime) => new
    {
        disposed = ReadField(runtime, "disposed"), running = ReadField(runtime, "running"),
        sceneReleased = ReadField(runtime, "scene") is null,
        attachmentEntriesReleased = ReadField(runtime, "attachments") is not { } attachments ||
            ((System.Collections.IDictionary)ReadField(attachments, "entries")!).Count == 0,
    };

    private static object HostExitState(object host)
    {
        var scheduler = ReadField(host, "motionScheduler")!;
        var timer = host.GetType().GetField("shellTimer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        return new
        {
            schedulerStopped = scheduler.GetType().GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scheduler) is false,
            shellTimerStopped = timer is System.Windows.Threading.DispatcherTimer shell ? !shell.IsEnabled : (bool?)null,
        };
    }

    private static object BinaryIdentity(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sample binaries must be loaded from this project output.");
        return new { name = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    }

    private static void WriteEvidence(SampleSession session, string name, object value)
    {
        try { File.WriteAllText(Path.Combine(session.DirectoryPath, name), JsonSerializer.Serialize(value)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed class SampleSession : IDisposable
{
    internal const string MutexName = @"Local\Dango.GaitSample";
    internal const string NativeFileName = "dango_physics_module.dll";
    internal const string ApprovedModuleSha256 = "00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C";
    internal string DirectoryPath { get; }
    internal string SettingsPath => Path.Combine(DirectoryPath, "fictional-settings.json");
    internal string ModulePath { get; }
    internal bool IsDisposed { get; private set; }
    private Mutex? mutex;

    private SampleSession(string directoryPath, string modulePath)
    {
        DirectoryPath = directoryPath;
        ModulePath = modulePath;
    }

    internal static string ValidateSessionPath(string repositoryRoot, string candidate)
    {
        var allowed = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "gait-sample")).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        if (!path.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(path) || File.Exists(path)) throw new InvalidDataException("A new in-workspace sample directory is required.");
        for (var parent = new DirectoryInfo(path).Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Sample directories cannot traverse a reparse point.");
        }
        return path;
    }
    internal static void ValidateModule(string modulePath)
    {
        if (!File.Exists(modulePath) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modulePath))) != ApprovedModuleSha256)
            throw new InvalidDataException("The verified physics module is required.");
    }
    internal static ControlCenterWindowSettings CreateSettings() => ControlCenterWindowSettings.CreateDefault() with
    {
        IntroSeen = true, DisplayMode = DesktopDisplayMode.Window, InteractionEnabled = true,
        ShowDebugSkeleton = false, KeyboardControl = new(true, KeyboardControlScheme.Wasd),
    };
    internal static SampleSession Create(string repositoryRoot, string candidate, string modulePath)
    {
        var path = ValidateSessionPath(repositoryRoot, candidate);
        ValidateModule(modulePath);
        var mutex = new Mutex(true, MutexName, out var created);
        if (!created) { mutex.Dispose(); throw new InvalidDataException("Another gait sample is running."); }
        var session = new SampleSession(path, modulePath) { mutex = mutex };
        try
        {
            _ = ValidateSessionPath(repositoryRoot, path);
            Directory.CreateDirectory(path);
            new ControlCenterWindowSettingsStore().Save(session.SettingsPath, CreateSettings());
            return session;
        }
        catch { session.Dispose(); throw; }
    }
    internal object?[] HostArguments => [false, false, SettingsPath, ModulePath, null];
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        mutex?.ReleaseMutex(); mutex?.Dispose(); mutex = null;
    }
}
