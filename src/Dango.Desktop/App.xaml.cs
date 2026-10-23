using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Native;

namespace Dango.Desktop;

internal enum DesktopStartupMode
{
    Product, PhysicsHostReview, EyeHatReview, Box2DNativeProbe, SmokeTest,
}

internal sealed record DesktopStartupOptions(DesktopStartupMode Mode, string? SettingsFile, string InstanceName);

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\Dango.Phase1.App";
    private SingleInstanceCoordinator? singleInstanceCoordinator;
    private DangoApplicationHost? applicationHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        var options = ParseStartupArguments(e.Args);
        base.OnStartup(e);

        if (options.Mode == DesktopStartupMode.Box2DNativeProbe)
        {
            Shutdown(RunBox2DNativeProbe());
            return;
        }

        if (options.Mode == DesktopStartupMode.SmokeTest)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(Shutdown), DispatcherPriority.ApplicationIdle);
            return;
        }

        var physicsReview = options.Mode == DesktopStartupMode.PhysicsHostReview;
        var eyeHatReview = options.Mode == DesktopStartupMode.EyeHatReview;
        var instanceName = options.InstanceName;
        singleInstanceCoordinator = SingleInstanceCoordinator.TryBecomePrimary(
            instanceName,
            () => Dispatcher.BeginInvoke(
                new Action(() => applicationHost?.ShowControlCenter()),
                DispatcherPriority.Normal));
        if (singleInstanceCoordinator is null)
        {
            var signaled = SingleInstanceCoordinator.TrySignalPrimary(instanceName);
            Shutdown(signaled ? 0 : 2);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        applicationHost = new DangoApplicationHost(physicsReview, eyeHatReview,
            settingsFile: options.SettingsFile);
        applicationHost.Start();
        if (physicsReview || eyeHatReview) applicationHost.ShowControlCenter();
    }

    internal static DesktopStartupOptions ParseStartupArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0) return new(DesktopStartupMode.Product, null, SingleInstanceName);
        // Data scopes select the ordinary product. They cannot be combined with a
        // diagnostic/preview mode, duplicate scope, or retired product route.
        if (args.Length == 2 && !string.IsNullOrWhiteSpace(args[1]) && !args[1].StartsWith("--", StringComparison.Ordinal))
        {
            var settingsFile = args[0] switch
            {
                "--data-root" => ProductSettingsFile(args[1]),
                "--local-install-acceptance-settings" => ResolveOwnedIsolatedSettingsFile(
                    args[1], ".dango-local-install-acceptance.json", "acceptanceFormat"),
                _ => throw new ArgumentException("Unknown Dango startup option: " + args[0]),
            };
            return new(DesktopStartupMode.Product, settingsFile, ProductInstanceName(settingsFile));
        }
        if (args.Length != 1) throw new ArgumentException("Choose one Dango startup mode or one explicit data scope.");
        var mode = args[0] switch
        {
            "--physics-host-review" => DesktopStartupMode.PhysicsHostReview,
            "--eye-hat-review" => DesktopStartupMode.EyeHatReview,
            "--box2d-native-probe" => DesktopStartupMode.Box2DNativeProbe,
            "--smoke-test" => DesktopStartupMode.SmokeTest,
            // Unknown/retired switches never fall through to the daily product.
            _ => throw new ArgumentException("Unknown or incomplete Dango startup option: " + args[0]),
        };
        var instanceName = mode switch
        {
            DesktopStartupMode.PhysicsHostReview => SingleInstanceName + ".PhysicsHostReview",
            DesktopStartupMode.EyeHatReview => SingleInstanceName + ".EyeHatReview",
            _ => SingleInstanceName,
        };
        return new(mode, null, instanceName);
    }

    private static string ProductSettingsFile(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Use --data-root with one absolute data directory.");
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (root == Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar))
            throw new ArgumentException("The data directory cannot be a drive root.");
        return Path.Combine(root, "settings.json");
    }

    internal static string? ResolveProductSettingsFile(string[] args)
    {
        var options = ParseStartupArguments(args);
        return args.Contains("--data-root", StringComparer.Ordinal) ? options.SettingsFile : null;
    }

    internal static string? ResolveLocalInstallAcceptanceSettingsFile(string[] args)
    {
        var options = ParseStartupArguments(args);
        return args.Contains("--local-install-acceptance-settings", StringComparer.Ordinal) ? options.SettingsFile : null;
    }

    internal static void ValidateStartupArguments(string[] args) => _ = ParseStartupArguments(args);

    private static string ResolveOwnedIsolatedSettingsFile(string directory, string ownerFile, string formatProperty)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var dailyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango");
        static bool IsWithin(string candidate, string parent) => candidate.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!Path.IsPathFullyQualified(directory) || root.Length < 3 || root[1] != ':' ||
            root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(component => component.EndsWith('.') || component.EndsWith(' ')) ||
            Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) == root ||
            IsWithin(root, dailyRoot) || IsWithin(dailyRoot, root) ||
            IsWithin(root, AppContext.BaseDirectory) || IsWithin(AppContext.BaseDirectory, root))
            throw new InvalidDataException("Acceptance settings must be outside the daily settings and package directories.");
        for (var cursor = root; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Acceptance settings cannot use reparse points.");
        var ownerPath = Path.Combine(root, ownerFile);
        if ((File.GetAttributes(ownerPath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Acceptance owner cannot be a directory or reparse point.");
        using var owner = JsonDocument.Parse(File.ReadAllText(ownerPath));
        if (owner.RootElement.EnumerateObject().Count() != 3 ||
            owner.RootElement.GetProperty("application").GetString() != "Dango" ||
            owner.RootElement.GetProperty(formatProperty).GetInt32() != 1 ||
            !owner.RootElement.GetProperty("fictional").GetBoolean())
            throw new InvalidDataException("The directory is not owned by a Dango local acceptance session.");
        var settingsPath = Path.Combine(root, "settings.json");
        var presetPath = ControlCenterWindowSettingsStore.GetPresetPath(settingsPath);
        var appearancePath = Path.Combine(root, "appearance.json");
        var accessoryPath = Path.Combine(root, "accessories.json");
        var accessoryV2Path = Path.Combine(root, "accessories-v2.json");
        var accessoryAssetsPath = Path.Combine(root, "accessory-assets");
        var accessoryProjectsPath = Path.Combine(root, "accessory-projects");
        // Enumerate entries rather than testing File.Exists: dangling links and
        // directory links at every settings filename must also fail before load.
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (entry.Equals(settingsPath, StringComparison.OrdinalIgnoreCase) ||
                entry.Equals(presetPath, StringComparison.OrdinalIgnoreCase) ||
                entry.Equals(appearancePath, StringComparison.OrdinalIgnoreCase) ||
                entry.Equals(accessoryPath, StringComparison.OrdinalIgnoreCase) ||
                entry.Equals(accessoryV2Path, StringComparison.OrdinalIgnoreCase))
                if ((File.GetAttributes(entry) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new InvalidDataException("Acceptance settings files cannot be directories or reparse points.");
            if (entry.Equals(accessoryAssetsPath, StringComparison.OrdinalIgnoreCase) ||
                entry.Equals(accessoryProjectsPath, StringComparison.OrdinalIgnoreCase))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                    throw new InvalidDataException("Acceptance accessory assets must use an ordinary owned directory.");
            }
        }
        return settingsPath;
    }

    internal static string ResolveSingleInstanceName(string[] args) => ParseStartupArguments(args).InstanceName;

    private static string ProductInstanceName(string settings)
    {
        var daily = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "settings.json");
        if (Path.GetFullPath(settings).Equals(daily, StringComparison.OrdinalIgnoreCase)) return SingleInstanceName;
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(settings).ToUpperInvariant())));
        return SingleInstanceName + ".Data." + identity[..24];
    }

    protected override void OnExit(ExitEventArgs e)
    {
        applicationHost?.Dispose();
        applicationHost = null;
        singleInstanceCoordinator?.Dispose();
        singleInstanceCoordinator = null;
        base.OnExit(e);
    }

    private static int RunBox2DNativeProbe()
    {
        if (!Box2DNativeLoader.TryLoad(
            Box2DNativeLoader.DefaultLibraryPath,
            Box2DNativeLoader.PinnedRequirements,
            out var session,
            out _))
        {
            return 20;
        }

        var loadedSession = session!;
        using (loadedSession)
        {
            return loadedSession.RunLifecycleProbe(8).Succeeded ? 0 : 21;
        }
    }
}
