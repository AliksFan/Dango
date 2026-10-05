using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Core.Coordinates;
using Dango.Physics;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FaceProductTests
{
    private static string NewPath()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "face-implementation", "product-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return Path.Combine(root, "appearance.json");
    }
    private static DangoAppearanceConfiguration Choice => new(new(0,0,0), new(0,0,0), new(137, -83, 33, 12.5));
    private static T E<T>(ProductHomeView home, string name) where T : class => (T)home.FindName(name);
    private static void Click(ProductHomeView home, string name) => E<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Field<T>(object host, string name) => (T)host.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
    private static ControlCenterWindow HomeWindow(DangoApplicationHost host) =>
        (ControlCenterWindow)host.GetType().GetMethod("GetOrCreateControlCenter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, null)!;
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    [STATestMethod]
    public void DialogCancelAndSliderPreviewNeverWriteThenApplyRestoresAllSixFields()
    {
        var path = NewPath(); var session = new AppearanceSettingsSession(path); var home = new ProductHomeView();
        home.BindAppearance(session); home.ShowEditor(HomeEditor.Face);
        home.AppearanceColorPicker = _ => null;
        Click(home, "BodyColorPickerButton"); Click(home, "EyeColorPickerButton");
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
        home.AppearanceColorPicker = _ => new(0,0,0);
        Click(home, "BodyColorPickerButton"); Click(home, "EyeColorPickerButton");
        E<Slider>(home,"EyeSizeSlider").Value = 137; E<Slider>(home,"EyeOvalSlider").Value = -83;
        E<Slider>(home,"EyeHeightSlider").Value = 33; E<Slider>(home,"EyeSpacingSlider").Value = 12.5;
        Assert.AreEqual(Choice, session.DraftAppearance); Assert.IsFalse(File.Exists(path));
        Click(home, "ApplyButton");
        Assert.AreEqual(Choice, session.SavedAppearance); Assert.AreEqual(Choice, new AppearanceSettingsSession(path).SavedAppearance);
        Assert.IsFalse(session.HasPendingSave);
        Click(home,"DefaultAppearanceButton"); Assert.AreEqual(DangoAppearanceConfiguration.Default,session.DraftAppearance);
        Assert.AreEqual(Choice,new AppearanceSettingsSession(path).SavedAppearance);
        Click(home,"CancelAppearanceButton"); Assert.AreEqual(Choice,session.DraftAppearance);
    }

    [STATestMethod]
    public void TabsRemainReusableAndAnOldDialogCannotReviveCancelledDraft()
    {
        var session = new AppearanceSettingsSession(NewPath()); var home = new ProductHomeView(); home.BindAppearance(session); home.ShowEditor(HomeEditor.Face);
        Click(home,"AppearanceEyesTab"); Click(home,"AppearanceColorsTab"); Click(home,"AppearanceEyesTab");
        Assert.AreEqual(Visibility.Visible,E<StackPanel>(home,"AppearanceEyesPanel").Visibility);
        home.AppearanceColorPicker = _ => { home.CloseEditor(); return new(0,0,0); };
        Click(home,"BodyColorPickerButton"); Assert.AreEqual(session.SavedAppearance,session.DraftAppearance); Assert.IsFalse(session.IsDirty);
    }

    [STATestMethod]
    [DataRow("Close")]
    [DataRow("Wardrobe")]
    [DataRow("Settings")]
    [DataRow("Hide")]
    public void LeavingFaceCancelsTheWholeDraft(string action)
    {
        var session = new AppearanceSettingsSession(NewPath()); session.StagePreview(Choice); Assert.IsTrue(session.TrySave());
        var home = new ProductHomeView { ReduceMotion = true }; home.BindAppearance(session);
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show(); home.ShowEditor(HomeEditor.Face); Click(home,"DefaultAppearanceButton");
            Assert.AreEqual(DangoAppearanceConfiguration.Default,session.DraftAppearance);
            switch(action) { case "Close": home.CloseEditor(); break; case "Wardrobe": home.ShowEditor(HomeEditor.Wardrobe); break; case "Settings": home.ShowSettingsPage(); break; default: window.Hide(); break; }
            Assert.AreEqual(Choice,session.DraftAppearance); Assert.IsFalse(session.IsDirty);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RealFileLockShowsFailureKeepsDraftAndAllowsSameButtonRetry()
    {
        var path=NewPath(); var session=new AppearanceSettingsSession(path); session.StagePreview(Choice); Assert.IsTrue(session.TrySave());
        var original=File.ReadAllBytes(path); var home=new ProductHomeView(); home.BindAppearance(session); home.ShowEditor(HomeEditor.Face);
        Click(home,"DefaultAppearanceButton");
        using (var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Click(home,"ApplyButton"); Assert.AreEqual(Choice,session.SavedAppearance); Assert.AreEqual(DangoAppearanceConfiguration.Default,session.DraftAppearance);
            CollectionAssert.AreEqual(original,File.ReadAllBytes(path)); Assert.IsTrue(E<Button>(home,"ApplyButton").IsEnabled);
            StringAssert.Contains(E<TextBlock>(home,"AppearanceStatus").Text,"未保存");
        }
        Click(home,"ApplyButton"); Assert.AreEqual(DangoAppearanceConfiguration.Default,new AppearanceSettingsSession(path).SavedAppearance);
    }

    [STATestMethod]
    public void HostLoadsBeforeInitializationAndMainSettingsSaveCannotApplyAppearance()
    {
        var path=NewPath(); var saved=new AppearanceSettingsSession(path); saved.StagePreview(Choice); Assert.IsTrue(saved.TrySave()); var original=File.ReadAllBytes(path);
        var settingsPath=Path.Combine(Path.GetDirectoryName(path)!,"settings.json");
        using(var host=new DangoApplicationHost(settingsFile:settingsPath,nativeLibraryPath:settingsPath+".missing.dll",isolatedFaceReview:true))
        {
            var runtime=Field<StackedDangoSceneRuntime>(host,"stackedRuntime"); Assert.AreEqual(Choice,runtime.EffectiveAppearance); Assert.AreEqual(0,runtime.EntityIds.Count);
            var home=HomeWindow(host).ProductHome; Assert.AreEqual(Choice,home.DraftAppearance); home.ShowEditor(HomeEditor.Face); Click(home,"DefaultAppearanceButton");
            host.GetType().GetMethod("SaveSettings",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(host,null);
            CollectionAssert.AreEqual(original,File.ReadAllBytes(path)); Assert.IsTrue(home.BoundAppearance!.IsDirty);
        }
        CollectionAssert.AreEqual(original,File.ReadAllBytes(path));
    }

    [STATestMethod]
    public void FocusedSlidersNeverMoveJumpOrEnterAndEscapeCancels()
    {
        var home=new ProductHomeView{ReduceMotion=true}; home.BindAppearance(new AppearanceSettingsSession(NewPath()));
        var window=new Window{Content=home,Width=1024,Height=640,Left=-10000,Top=-10000,ShowInTaskbar=false};
        try
        {
            window.Show(); home.ShowEditor(HomeEditor.Face); Click(home,"AppearanceEyesTab"); E<Slider>(home,"EyeHeightSlider").Focus();
            foreach(var key in new[]{Key.A,Key.D,Key.S,Key.Space,Key.W})
            {
                var args=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent}; home.RaiseEvent(args);
                Assert.IsTrue(home.IsEditing); Assert.IsFalse(home.IsInPorch); Assert.AreEqual(HomeEditor.Face,home.SelectedEditor); Assert.IsFalse(args.Handled);
            }
            var esc=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent}; home.RaiseEvent(esc);
            Assert.IsTrue(esc.Handled); Assert.IsFalse(home.IsEditing);
        }
        finally{window.Close();}
    }

    [STATestMethod]
    public void BindingPreservesFallbackAndOnlyCustomizesPrimaryEntity()
    {
        var body = new DeformableDangoVisual(); var hat = new DangoHatVisual();
        var single = new StackedPhysicsPresentationFrame([EyeMotionTests.Body()], [new LogicalRect(0,0,160,120)], [new LogicalVector(0,0)], 0, [], true, 0);
        DangoHostFrameBinding.Apply(body,hat,single,0,bodyFillOverride:System.Windows.Media.Brushes.Black,eyeFillOverride:System.Windows.Media.Brushes.Black,appearanceEntity:new(1));
        Assert.AreEqual(Visibility.Visible,body.Visibility);
        var pair = new StackedPhysicsPresentationFrame([EyeMotionTests.Body(),EyeMotionTests.Body()], [new LogicalRect(0,0,160,120),new LogicalRect(160,0,160,120)], [new LogicalVector(0,0),new LogicalVector(0,0)], 0, [], false, 0)
            {EntityIds=[new(1),new(2)],ColorSlots=[0,1]};
        DangoHostFrameBinding.Apply(body,hat,pair,0,bodyFillOverride:System.Windows.Media.Brushes.Black,eyeFillOverride:System.Windows.Media.Brushes.Black,appearanceEntity:new(1));
        Assert.AreSame(System.Windows.Media.Brushes.Black,body.BodyFill); Assert.AreSame(System.Windows.Media.Brushes.Black,body.EyeFill);
        DangoHostFrameBinding.Apply(body,hat,pair,1,bodyFillOverride:System.Windows.Media.Brushes.Black,eyeFillOverride:System.Windows.Media.Brushes.Black,appearanceEntity:new(1));
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(201,226,255),((SolidColorBrush)body.BodyFill).Color);
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(0x2B,0x20,0x1E),((SolidColorBrush)body.EyeFill).Color);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SharedNativeEntityKeepsAppearanceAcrossFramesRoomAndRegeneration()
    {
        var path=NewPath(); var session=new AppearanceSettingsSession(path); session.StagePreview(Choice); Assert.IsTrue(session.TrySave());
        using var host=new DangoApplicationHost(settingsFile:Path.Combine(Path.GetDirectoryName(path)!,"settings.json"),isolatedFaceReview:true);
        var runtime=Field<StackedDangoSceneRuntime>(host,"stackedRuntime"); Assert.IsTrue(runtime.TryInitialize(),runtime.LastError);
        host.ShowControlCenter(); Pump(350); var home=HomeWindow(host).ProductHome; var id=runtime.EntityIds.Single(); var physics=runtime.LastPhysicsFrame; var frame=runtime.LastFrame; var owner=runtime.FrameDriver;
        home.ShowEditor(HomeEditor.Face); E<Slider>(home,"EyeSizeSlider").Value=110;
        Assert.AreSame(physics,runtime.LastPhysicsFrame); Assert.AreSame(frame,runtime.LastFrame); Assert.AreSame(owner,runtime.FrameDriver); Assert.AreEqual(id,runtime.EntityIds.Single());
        Click(home,"PreviewSleepButton"); Pump(100);
        Assert.IsTrue(runtime.LastFrame!.Bodies.Single().Attachments!.Face.Left.Openness <= .03, "The existing eyelid spring must settle into the sleeping threshold.");
        Click(home,"PreviewSleepButton"); Pump(200);
        Assert.IsTrue(runtime.LastFrame!.Bodies.Single().Attachments!.Face.Left.Openness > .9);
        home.CloseEditor(); Assert.AreEqual(Choice,runtime.EffectiveAppearance);
        host.EnterLocalRoom(); Pump(500); Assert.AreEqual(id,runtime.EntityIds.Single()); Assert.AreEqual(Choice,runtime.EffectiveAppearance); Assert.IsFalse(home.OwnsLiveFrameDriver);
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(0,0,0),((SolidColorBrush)Field<DeformableDangoVisual>(host.LocalWindow!.Scene,"body").EyeFill).Color);
        host.SwitchLocalRoomPresentation(LocalRoomPresentation.Wallpaper);
        for (var wait = 0; host.LocalHandoffPending && wait < 40; wait++) Pump(100);
        Assert.AreEqual(LocalRoomPresentation.Wallpaper,host.LocalPresentation,host.LocalRoomFailure);
        var wallpaper=Field<LocalRoomWallpaperHost>(host,"localRoomWallpaper");
        Assert.IsTrue(wallpaper.Scene.HasHealthyFrame); Assert.AreEqual(id,runtime.EntityIds.Single());
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(0,0,0),((SolidColorBrush)Field<DeformableDangoVisual>(wallpaper.Scene,"body").BodyFill).Color);
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(0,0,0),((SolidColorBrush)Field<DeformableDangoVisual>(wallpaper.Scene,"body").EyeFill).Color);
        host.ReturnToPorch(); Pump(500); Assert.IsTrue(home.OwnsLiveFrameDriver); Assert.AreEqual(id,runtime.EntityIds.Single()); Assert.AreEqual(Choice,runtime.EffectiveAppearance);
        runtime.RegenerateDango(); Pump(350); Assert.AreEqual(Choice,runtime.EffectiveAppearance); Assert.AreEqual(id,runtime.EntityIds.Single());
    }
}
