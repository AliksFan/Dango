using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>File-selection results go through the real Create action and real image modal; no ImageEditor stub.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AccessoryImageEditorCrashTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    public void OversizedPngSelectedFromCreateIsReportedWithoutOpeningTheImageModalOrChangingTheDraft()
    {
        using var fixture = CreateFixture(2049, 8);
        var before = fixture.Session.DraftConfiguration;
        var sourceBytes = File.ReadAllBytes(fixture.Source);
        Assert.IsFalse(typeof(IOException).IsAssignableFrom(typeof(InvalidDataException)),
            "The reproduction depends on InvalidDataException not being covered by the old IOException filter.");
        TestContext.WriteLine("Owned oversized transparent PNG: " + fixture.Source);
        TestContext.WriteLine("Invoking the actual Create button with its selected file path; image-modal callback is not injected.");

        CreateButton(fixture.Panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.AreEqual(before, fixture.Session.DraftConfiguration, "Rejected input must leave every existing draft entry unchanged.");
        Assert.AreEqual(before, fixture.Session.SavedConfiguration);
        Assert.IsFalse(fixture.Session.IsDirty);
        Assert.IsTrue(fixture.Owner.IsVisible, "The owned host must remain usable after an import validation error.");
        Assert.IsNull(FindOwnedModal(fixture.Owner), "A rejected image must never reach the image editor.");
        StringAssert.Contains(fixture.Panel.StatusMessage, "操作未完成");
        StringAssert.Contains(fixture.Panel.StatusMessage, "2048");
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(fixture.Root)));
        CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(fixture.Source));
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void AcceptedTransparentFileOpensAndRendersTheRealModalBeforeConfirmOrCancel(bool accept)
    {
        using var fixture = CreateFixture(320, 192);
        var saved = fixture.Session.SavedConfiguration;
        var originalSource = File.ReadAllBytes(fixture.Source);
        Exception? failure = null;
        var observedModal = false;
        var actionInvoked = false;
        AccessoryImageEditorWindow? imageEditor = null;
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            try
            {
                imageEditor ??= FindOwnedModal(fixture.Owner);
                if (watch.Elapsed > TimeSpan.FromSeconds(12))
                    throw new AssertFailedException("The owned Create action did not complete its real modal lifecycle in 12 seconds.");
                if (imageEditor is null || !imageEditor.IsLoaded || !imageEditor.IsVisible || actionInvoked) return;
                observedModal = true;
                Assert.AreSame(fixture.Owner, imageEditor.Owner);
                Assert.IsFalse(IsWindowEnabled(new WindowInteropHelper(fixture.Owner).Handle),
                    "ShowDialog must actually disable the owner, not merely show a modeless lookalike.");
                imageEditor.UpdateLayout();
                var surface = VisualDescendants(imageEditor).OfType<AccessoryImageEditorSurface>().Single();
                Assert.IsTrue(surface.IsLoaded && surface.ActualWidth > 0 && surface.ActualHeight > 0,
                    "The imported PNG surface must complete its actual Loaded and measure/arrange path.");
                CaptureAndVerifyImportedPixels(surface, fixture.Run, accept ? "confirm" : "cancel");
                TestContext.WriteLine("Real image modal Loaded; measured surface " + surface.RenderSize + "; imported PNG pixels rendered.");

                var numbers = VisualDescendants(imageEditor).OfType<TextBox>().ToDictionary(AutomationProperties.GetName);
                numbers["配饰尺寸 % 数值"].Text = "185";
                numbers["基础角度 ° 数值"].Text = "37";
                numbers["图片支点 X 数值"].Text = "0.23";
                numbers["图片支点 Y 数值"].Text = "0.68";
                var action = VisualDescendants(imageEditor).OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == (accept ? "确认" : "取消"));
                Assert.IsTrue(action.IsEnabled && action.IsVisible);
                var bounds = action.TransformToAncestor(imageEditor).TransformBounds(new Rect(action.RenderSize));
                var hit = imageEditor.InputHitTest(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)) as DependencyObject;
                Assert.IsNotNull(hit);
                Assert.IsTrue(ReferenceEquals(hit, action) || action.IsAncestorOf(hit), "The modal action must have a real unobstructed hit target.");
                actionInvoked = true;
                ((IInvokeProvider)new ButtonAutomationPeer(action).GetPattern(PatternInterface.Invoke)!).Invoke();
            }
            catch (Exception error)
            {
                failure = error;
                timer.Stop();
                imageEditor?.Close();
            }
        };
        DispatcherUnhandledExceptionEventHandler unexpected = (_, args) =>
        {
            // Capture failures only in this test's own modal dispatcher so a render bug becomes a failed test.
            failure = args.Exception;
            args.Handled = true;
            timer.Stop();
            (FindOwnedModal(fixture.Owner) ?? imageEditor)?.Close();
        };
        fixture.Owner.Dispatcher.UnhandledException += unexpected;
        timer.Start();
        try
        {
            CreateButton(fixture.Panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally
        {
            timer.Stop();
            fixture.Owner.Dispatcher.UnhandledException -= unexpected;
            imageEditor?.Close();
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.IsTrue(observedModal && actionInvoked, "The file must go through the real image editor, without an injected result delegate.");
        Assert.IsTrue(IsWindowEnabled(new WindowInteropHelper(fixture.Owner).Handle), "Closing the modal must restore its owner input.");
        Assert.HasCount(saved.Items.Count + 1, fixture.Session.DraftConfiguration.Items);
        var imported = fixture.Panel.SelectedItem!;
        Assert.AreEqual(accept ? 185 : 100, imported.SizePercent, 1e-9);
        Assert.AreEqual(accept ? 37 : 0, imported.BaseAngleDegrees, 1e-9);
        Assert.AreEqual(accept ? .23 : .5, imported.ImagePivotX, 1e-9);
        Assert.AreEqual(accept ? .68 : .5, imported.ImagePivotY, 1e-9);
        Assert.AreEqual(saved, fixture.Session.SavedConfiguration, "Modal confirmation must return only a wardrobe draft.");
        Assert.IsTrue(fixture.Session.IsDirty);
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(fixture.Root)));
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetAssetPath(fixture.Root, imported.AssetId)));
        CollectionAssert.AreEqual(originalSource, File.ReadAllBytes(fixture.Source));
        fixture.Panel.CancelDraft();
        Assert.AreEqual(saved, fixture.Session.DraftConfiguration);
        Assert.IsFalse(fixture.Session.IsDirty);
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(fixture.Root)));
    }

    private static Fixture CreateFixture(int width, int height)
    {
        var run = Path.Combine(TestRepository.Root, "artifacts", "accessory-ui-v3-crash-harness", "runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var source = WritePng(run, width, height);
        var root = Path.Combine(run, "review");
        var session = new AccessorySettingsSession(root);
        var panel = new AccessoryEditorPanel();
        panel.Bind(session);
        panel.ImagePicker = () => source;
        var owner = new Window
        {
            Content = panel,
            Width = 320,
            Height = 640,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Fictional accessory import crash verification",
        };
        owner.Show();
        owner.UpdateLayout();
        return new(run, root, source, session, panel, owner);
    }

    private static string WritePng(string run, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var index = (y * width + x) * 4;
                pixels[index] = 170;
                pixels[index + 1] = 90;
                pixels[index + 2] = 50;
                pixels[index + 3] = x < width / 3 ? (byte)128 : (byte)255;
            }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var path = Path.Combine(run, $"fictional-transparent-{width}x{height}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static void CaptureAndVerifyImportedPixels(AccessoryImageEditorSurface surface, string run, string action)
    {
        var dpi = VisualTreeHelper.GetDpi(surface);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert.IsTrue(Enumerable.Range(0, pixels.Length / 4).Any(index => pixels[index * 4] > pixels[index * 4 + 2] + 30),
            "The real image surface must contain the fictional blue PNG material, not only its checkerboard placeholder.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(run, "actual-modal-surface-" + action + ".png"));
        encoder.Save(stream);
    }

    private static AccessoryImageEditorWindow? FindOwnedModal(Window owner) => PresentationSource.CurrentSources
        .OfType<HwndSource>().Select(source => source.RootVisual).OfType<AccessoryImageEditorWindow>()
        .SingleOrDefault(window => ReferenceEquals(window.Owner, owner));

    private static Button CreateButton(AccessoryEditorPanel panel) => VisualDescendants(panel.ActionTools).OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == "创建新配饰");

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in VisualDescendants(child)) yield return nested;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);

    private sealed record Fixture(string Run, string Root, string Source, AccessorySettingsSession Session,
        AccessoryEditorPanel Panel, Window Owner) : IDisposable
    {
        public void Dispose()
        {
            FindOwnedModal(Owner)?.Close();
            Owner.Close();
        }
    }
}
