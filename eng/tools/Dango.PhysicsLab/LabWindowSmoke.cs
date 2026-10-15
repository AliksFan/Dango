using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dango.PhysicsLab;

internal static class LabWindowSmoke
{
    internal static async Task Run(LabWindow window, string session)
    {
        var checks = new List<string>();
        try
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            window.PrepareSmoke();
            void Check(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); checks.Add(name); }
            Check(window.IsLoaded && window.IsVisible && !window.HasFault, "真实WPF窗口加载并连接实验DLL");
            for (var i = 0; i < 300; i++) window.StepOnce();
            Check(!window.HasFault && window.CurrentBody?.IsGrounded == true, "真实物理静置落地");
            window.ApplyButton.BringIntoView();
            window.InjectKey(Key.D, true, window.ApplyButton); window.StepOnce();
            Check(window.CurrentInput.Horizontal == 1, "普通按钮焦点不阻断D");
            var clicks = window.ButtonClicks;
            window.InjectKey(Key.Space, true, window.ApplyButton); window.StepOnce();
            Check(window.CurrentInput.JumpPressed, "按钮焦点Space产生跳跃边沿");
            window.StepOnce(); Check(!window.CurrentInput.JumpPressed, "长按Space不重复跳");
            window.InjectKey(Key.Space, false, window.ApplyButton); window.InjectKey(Key.D, false, window.ApplyButton); window.StepOnce();
            Check(window.ButtonClicks == clicks && window.CurrentInput.Horizontal == 0, "Space松开不触发应用按钮且D释放");
            window.TestSlider.BringIntoView();
            window.InjectKey(Key.W, true, window.TestSlider); window.StepOnce();
            Check(window.CurrentInput.Stretch == 1, "滑块焦点W控制拉伸");
            window.InjectKey(Key.S, true, window.TestSlider); window.StepOnce();
            Check(window.CurrentInput.InputActive && window.CurrentInput.Stretch == 0 && window.CurrentInput.Downward == 0 && window.CurrentInput.CancelForces, "W加S立即撤旧力且仍手动占用");
            window.InjectKey(Key.W, false, window.TestSlider); window.InjectKey(Key.S, false, window.TestSlider); window.StepOnce();
            window.Notes.BringIntoView(); window.Notes.Focus();
            window.InjectKey(Key.A, true, window.Notes); window.StepOnce();
            Check(window.CurrentInput.Horizontal == 0 && !window.CurrentInput.InputActive, "文字焦点不控制团子");
            window.InjectKey(Key.A, false, window.Notes);
            window.ApplyButton.BringIntoView();
            window.InjectKey(Key.D, true, window.ApplyButton); window.StepOnce();
            window.CancelInput(); window.StepOnce();
            Check(window.CurrentInput.Horizontal == 0 && window.CurrentInput.CancelForces, "取消立即撤销控制");
            window.InjectKey(Key.D, true, window.ApplyButton); window.StepOnce();
            Check(window.CurrentInput.Horizontal == 0, "取消后未松开不能补发");
            window.InjectKey(Key.D, false, window.ApplyButton);
            window.InjectKey(Key.D, true, window.ApplyButton); window.StepOnce();
            var beforeDisplayToggle = window.CurrentBody; var beforeDisplaySteps = window.NativeSteps;
            window.SetDisplaySmoothing(false); window.SetDisplaySmoothing(true);
            Check(ReferenceEquals(beforeDisplayToggle, window.CurrentBody) && window.NativeSteps == beforeDisplaySteps,
                "显示开关不重建或推进物理");
            window.StepOnce();
            Check(window.CurrentInput.Horizontal == 1, "显示开关不取消正在进行的移动");
            window.InjectKey(Key.D, false, window.ApplyButton);
            Check(window.ApplyButton.FocusVisualStyle is null && window.TestSlider.FocusVisualStyle is null, "非文字控件无虚线焦点装饰");
            var beforeEnter = window.ButtonClicks;
            window.InjectKey(Key.Enter, true, window.ApplyButton); window.InjectKey(Key.Enter, false, window.ApplyButton);
            Check(window.ButtonClicks == beforeEnter + 1, "按钮Enter仍显式执行");
            for (var i = 0; i < 300; i++) window.StepOnce();
            window.InjectKey(Key.F, true, window.ApplyButton); window.StepOnce();
            Check(window.InteractionCount == 0, "F远离目标无动作");
            window.InjectKey(Key.F, false, window.ApplyButton);
            if (!window.IsContinuousGround)
            {
                window.InjectKey(Key.A, true, window.ApplyButton);
                for (var i = 0; i < 400 && window.CurrentBody!.CoreX > -2.3; i++) window.StepOnce();
                window.InjectKey(Key.A, false, window.ApplyButton);
                window.InjectKey(Key.F, true, window.ApplyButton); window.StepOnce();
                Check(window.InteractionCount == 1 && !window.HasFault, "F近处触发真实平台操作");
                window.InjectKey(Key.F, true, window.ApplyButton); window.StepOnce();
                Check(window.InteractionCount == 1, "F持有不重复交互");
                window.InjectKey(Key.F, false, window.ApplyButton);
            }
            else
            {
                window.InjectKey(Key.D, true, window.ApplyButton); window.StepOnce();
                window.SelectGroundForSmoke(false); window.StepOnce();
                Check(!window.IsContinuousGround && window.CurrentInput.Horizontal == 0 && !window.HasFault, "切回离散基线清输入并重建世界");
                window.InjectKey(Key.D, false, window.ApplyButton);
                window.SelectGroundForSmoke(true); window.StepOnce();
                Check(window.IsContinuousGround && window.CurrentInput.Horizontal == 0 && !window.HasFault, "连续模型重新进入不补发旧按键");
            }
            if ((int)window.CurrentDrive >= 4)
            {
                window.SelectDriveForSmoke(Dango.Physics.DangoLabDrive.DirectionRegion);
                for (var i=0;i<300;i++) window.StepOnce();
                window.InjectKey(Key.D,true,window.ApplyButton); window.StepOnce();
                Check(window.CurrentRegionStats is { ActualForceX: >0, HorizontalMask: >0 }, "方向区域模式产生右侧真实施力诊断");
                window.InjectKey(Key.D,false,window.ApplyButton);
                window.SelectDriveForSmoke(Dango.Physics.DangoLabDrive.DirectionPoint);
                for (var i=0;i<300;i++) window.StepOnce();
                window.InjectKey(Key.W,true,window.ApplyButton); window.StepOnce();
                Check(window.CurrentRegionStats is { ActualForceY: >0 } selected && System.Numerics.BitOperations.PopCount((uint)selected.VerticalMask)==1,
                    "最近单节点模式从上部向上施力");
                window.InjectKey(Key.W,false,window.ApplyButton);
                window.SelectDriveForSmoke(Dango.Physics.DangoLabDrive.DirectionRegion);
            }
            window.ResetWorld(); for (var i = 0; i < 300; i++) window.StepOnce();
            window.ShowControlsForSmoke();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            window.Paint(forceInformation: true); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(session, "window.png"))) encoder.Save(file);
            File.WriteAllText(Path.Combine(session, "wpf-smoke.json"), JsonSerializer.Serialize(new
            { passed = true, checks, nativeSteps = window.NativeSteps, method = "shown WPF routed events; no OS keyboard automation", manualAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
            window.Close();
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(session, "wpf-smoke.json"), JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(11);
        }
    }
}
