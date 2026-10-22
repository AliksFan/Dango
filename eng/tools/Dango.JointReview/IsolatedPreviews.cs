using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Native;
using Dango.JointReview.Previews;

namespace Dango.JointReview;

internal static class IsolatedPreviews
{
    // Frozen legacy module identity from eng/native/box2d/dependency.json, not the product Belt module.
    private const string LegacySha256 = "6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A";
    private static readonly string[] Modes =
        ["--home-ui-review", "--settings-ui-review", "--soft-body-preview", "--paired-contact-preview"];

    internal static bool IsMode(string mode) => Modes.Contains(mode) || mode == "--verify-isolated-previews";

    internal static void RequireLegacyNative()
    {
        var path = Box2DNativeLoader.DefaultLibraryPath;
        if (!File.Exists(path)) throw new FileNotFoundException("旧模块独立预览需要 dango_box2d.dll；窗口尚未构造。", path);
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!string.Equals(actual, LegacySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"旧模块独立预览 SHA256 不匹配：expected {LegacySha256}, actual {actual}；窗口尚未构造。");
        if (!Box2DNativeLoader.TryLoad(path, Box2DNativeLoader.PinnedRequirements, out var session, out var failure))
            throw new InvalidOperationException($"旧模块独立预览不可用：{failure.Kind}: {failure.Message}；窗口尚未构造。");
        session!.Dispose();
    }

    internal static int Run(string[] args)
    {
        var verify = args[0] == "--verify-isolated-previews";
        if (args.Length != (verify ? 2 : 1) || (verify && string.IsNullOrWhiteSpace(args[1])))
            throw new ArgumentException("独立预览仅接受单个模式；无窗口检查使用 --verify-isolated-previews OUTPUT_DIRECTORY。");
        var output = verify ? ValidateNewOutput(args[1]) : null;
        if (verify || args[0] is "--soft-body-preview" or "--paired-contact-preview") RequireLegacyNative();
        var app = new Application { ShutdownMode = verify ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose };
        if (!verify) return app.Run(Create(args[0]));
        try { Verify(output!); return 0; }
        finally { app.Shutdown(); }
    }

    private static string ValidateNewOutput(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Directory.Exists(full) || File.Exists(full))
            throw new IOException("检查输出必须是新目录，已有文件或目录不会覆盖。");
        for (var ancestor = Path.GetDirectoryName(full); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
        {
            if ((Directory.Exists(ancestor) || File.Exists(ancestor)) &&
                (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("检查输出路径不能包含reparse point。");
        }
        return full;
    }

    private static Window Create(string mode)
    {
        if (mode == "--soft-body-preview") return new SoftBodyPreviewWindow();
        if (mode == "--paired-contact-preview") return new PairedContactPreviewWindow();
        var settings = mode == "--settings-ui-review";
        var home = new ProductHomeView();
        home.SettingsButton.ToolTip = "设置界面预览 · 未连接桌面";
        if (settings) home.ShowSettingsPage();
        return new Window
        {
            Title = settings ? "Dango · 设置页视觉预览" : "Dango · 首页交互验收",
            Width = 1024, Height = 640, MinWidth = 336, MinHeight = 560,
            Content = home, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
    }

    private static object? Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    private static void Verify(string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>();
        foreach (var mode in Modes)
        {
            var window = Create(mode);
            // Capture the actual production world so release checks cannot pass merely by dropping the controller reference.
            var world = window switch
            {
                SoftBodyPreviewWindow => Field(Field(window, "controller")!, "world"),
                PairedContactPreviewWindow => Field(window, "world"),
                _ => null,
            };
            try
            {
                var surface = (FrameworkElement)window.Content;
                surface.Measure(new Size(window.Width, window.Height));
                surface.Arrange(new Rect(0, 0, window.Width, window.Height));
                surface.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if (window is SoftBodyPreviewWindow soft) soft.VerifyNativeFrame();
                if (window is PairedContactPreviewWindow paired) paired.VerifyNativeFrame();
                if (window.IsVisible || window.IsActive || window.Topmost)
                    throw new InvalidOperationException("无窗口检查意外显示或激活窗口。");
            }
            finally { window.Close(); }
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var released = window switch
            {
                SoftBodyPreviewWindow soft => soft.ResourcesReleased && Field(world!, "_session") is null && Field(world!, "_softBody") is null,
                PairedContactPreviewWindow paired => paired.ResourcesReleased && Field(world!, "session") is null && Field(world!, "pairedSoftBodies") is null,
                _ => true,
            };
            if (!released || window.IsVisible || Application.Current.Windows.Cast<Window>().Contains(window))
                throw new InvalidOperationException($"{mode} 关闭后仍持有timer、native session或窗口。");
            results.Add(new { mode, constructed = true, layout = true, nativeFrame = world is not null, resourcesReleased = released });
        }
        using var report = new FileStream(Path.Combine(output, "isolated-previews.json"), FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(report, new
        {
            scope = "Unshown construction, layout, native frame and close/release only; foreground rendering and input NOT verified",
            nativeSha256 = LegacySha256,
            results,
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
