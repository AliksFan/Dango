using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Client.Sync;
using Dango.Core.Coordinates;
using Dango.Desktop.Multiplayer;
using Dango.Protocol;

namespace Dango.MultiplayerSample;

internal sealed class MultiplayerWindow : Window
{
    private readonly string directory;
    private readonly RoomConnection connection;
    private readonly NetworkScenePresenter presenter;
    private readonly NetworkInputRouter input;
    private readonly StreamReader controls;
    private readonly StreamWriter reports;
    private readonly Canvas scene = new() { Background = new SolidColorBrush(Color.FromRgb(226, 242, 252)), ClipToBounds = true };
    private readonly TextBlock status = new() { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, NetworkPetVisual> visuals = [];
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private TimeSpan lastFrame;
    private long renderedFrames;
    private int fallbackPets;
    private bool closing;
    private bool disposed;
    private bool visibilitySync;
    private bool wasPaused = true;
    private long lastConnectionFence;
    public bool HasFailure { get; private set; }

    public MultiplayerWindow(string role, string directory, LocalClientBootstrap bootstrap,
        AnonymousPipeClientStream controls, AnonymousPipeClientStream reports, bool fallback)
    {
        this.directory = directory;
        this.controls = new StreamReader(controls);
        this.reports = new StreamWriter(reports) { AutoFlush = true };
        connection = new RoomConnection(bootstrap);
        presenter = new NetworkScenePresenter(Path.Combine(AppContext.BaseDirectory,
            fallback ? "deliberately-unavailable-module.dll" : "dango_physics_module.dll"));
        input = new NetworkInputRouter(direction => connection.SetMoveIntent(direction), () => connection.StartWave(),
            () => !visibilitySync && connection.Replica.Frame.State == ReplicaState.Live);
        Title = $"Dango 多人小样 · 客户端 {role}";
        Width = 710; Height = 470; MinWidth = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var work = SystemParameters.WorkArea;
        Left = role == "A" ? work.Left + 20 : Math.Max(work.Left + 50, work.Right - Width - 20);
        Top = work.Top + (role == "A" ? 40 : 110);
        var layout = new DockPanel { Background = Brushes.White };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 10) };
        Button Add(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 7, 16, 7) };
            button.Click += (_, _) => action(); buttons.Children.Add(button); return button;
        }
        Add("← 左", input.MoveLeft); Add("右 →", input.MoveRight); Add("停止", input.Stop); Add("招呼", input.Wave);
        Add("退出", Close);
        var hint = new TextBlock { Text = "只控制自己的团子 · 方向键 / 空格招呼 · 丢焦点停止\n内存房间 · 不含碰撞或数据库 · 关闭两个窗口后服务正常退出", Margin = new Thickness(12, 5, 12, 10) };
        DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        DockPanel.SetDock(buttons, Dock.Top); layout.Children.Add(buttons);
        DockPanel.SetDock(hint, Dock.Bottom); layout.Children.Add(hint);
        layout.Children.Add(scene); Content = layout;
        Activated += (_, _) => input.SetActive(true);
        Deactivated += (_, _) => input.SetActive(false);
        StateChanged += (_, _) => PauseIfHidden();
        IsVisibleChanged += (_, _) => PauseIfHidden();
        PreviewKeyDown += (_, e) =>
        {
            if (!IsActive || e.IsRepeat) return;
            if (e.Key == Key.Left) { input.MoveLeft(); e.Handled = true; }
            if (e.Key == Key.Right) { input.MoveRight(); e.Handled = true; }
            if (e.Key == Key.Space) { input.Wave(); e.Handled = true; }
        };
        PreviewKeyUp += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right) { input.Stop(); e.Handled = true; }
        };
        Loaded += async (_, _) =>
        {
            CompositionTarget.Rendering += Render;
            _ = ReadControlsAsync();
            try { await connection.StartAsync(); }
            catch { status.Text = "连接失败，请查看无凭证诊断并重新开启会话。"; }
        };
        Closing += OnClosing;
    }

    private async void PauseIfHidden()
    {
        if (closing) return;
        var paused = !IsVisible || WindowState == WindowState.Minimized;
        presenter.SetPaused(paused);
        input.SetActive(false);
        lastFrame = elapsed.Elapsed;
        var needsBarrier = wasPaused && !paused;
        wasPaused = paused;
        if (needsBarrier && connection.Replica.Frame.State is ReplicaState.Live or ReplicaState.Resyncing)
        {
            visibilitySync = true;
            try { await connection.SynchronizeAsync(); }
            catch { /* The connection owns the stable error code and recovery state. */ }
            finally { visibilitySync = false; }
        }
        if (!closing) input.SetActive(!paused && IsActive);
    }

    private void Render(object? sender, EventArgs args)
    {
        if (closing || visibilitySync || !IsVisible || WindowState == WindowState.Minimized) return;
        var now = elapsed.Elapsed;
        var dt = now - lastFrame; lastFrame = now;
        var frame = connection.Replica.Frame;
        if (frame.State != ReplicaState.Live || frame.ConnectionFence != lastConnectionFence) input.Clear();
        lastConnectionFence = frame.ConnectionFence;
        input.Tick(now);
        var snapshot = frame.Snapshot;
        status.Text = $"{frame.State}  · 自己：{snapshot?.SelfPetId ?? "待加入"}  · sequence {frame.LastSequence}\n" +
            $"epoch {snapshot?.RoomEpoch ?? "—"}  · 本地软体回退 {fallbackPets}/2";
        if (snapshot is null || scene.ActualWidth <= 0 || scene.ActualHeight <= 0) return;
        var sampled = presenter.Sample(snapshot, connection.EstimatedServerTimeMs,
            new LogicalRect(0, 0, scene.ActualWidth, scene.ActualHeight), dt);
        fallbackPets = sampled.Count(p => p.IsFallback);
        foreach (var pet in sampled)
        {
            if (!visuals.TryGetValue(pet.PetId, out var visual))
            {
                visual = new NetworkPetVisual(); visuals.Add(pet.PetId, visual); scene.Children.Add(visual);
            }
            visual.ApplyFrame(pet);
            Canvas.SetLeft(visual, pet.Bounds.X); Canvas.SetTop(visual, pet.Bounds.Y);
        }
        renderedFrames++;
    }

    private async Task ReadControlsAsync()
    {
        try
        {
            while (!closing && await controls.ReadLineAsync() is { } line)
            {
                var command = JsonSerializer.Deserialize<SampleCommand>(line, RoomProtocol.Json);
                if (command is null) continue;
                await Dispatcher.InvokeAsync(async () =>
                {
                    var result = await ExecuteAsync(command);
                    await reports.WriteLineAsync(JsonSerializer.Serialize(result, RoomProtocol.Json));
                }).Task.Unwrap();
            }
            if (!closing) await Dispatcher.InvokeAsync(Close);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or TaskCanceledException)
        { if (!closing) await Dispatcher.InvokeAsync(Close); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HasFailure = true;
            SessionFiles.Write(directory, "control-failure.json", new { code = "control_failed", kind = exception.GetType().Name });
            if (!closing) await Dispatcher.InvokeAsync(Close);
        }
    }

    private async Task<SampleReport> ExecuteAsync(SampleCommand command)
    {
        var success = true;
        try
        {
            switch (command.Type)
            {
                case "move":
                    Activate(); input.SetActive(IsActive);
                    if (command.Direction == "left") input.MoveLeft();
                    else if (command.Direction == "right") input.MoveRight();
                    else input.Stop();
                    break;
                case "wave": Activate(); input.SetActive(IsActive); input.Wave(); break;
                case "disconnect": input.Clear(); await connection.DisconnectAsync(); break;
                case "reconnect": input.Clear(); await connection.ReconnectAsync(); break;
                case "minimize": WindowState = WindowState.Minimized; break;
                case "restore": WindowState = WindowState.Normal; Activate(); break;
                case "capture":
                    var content = (FrameworkElement)Content;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(directory, "window.png"))) encoder.Save(file);
                    break;
                case "close": _ = Dispatcher.BeginInvoke(Close); break;
                case "snapshot": break;
                default: success = false; break;
            }
        }
        catch { success = false; }
        var frame = connection.Replica.Frame;
        var time = command.ServerTimeMs ?? connection.EstimatedServerTimeMs;
        var snapshot = frame.Snapshot;
        if (snapshot is not null) snapshot = snapshot with { ServerTimeMs = time, Pets = snapshot.Pets.Select(p => RoomSampling.At(p, time)).ToArray() };
        return new(command.Id, success, success ? "ok" : "control_failed", Environment.ProcessId, frame.State.ToString(),
            snapshot, time, renderedFrames, fallbackPets, IsVisible && WindowState != WindowState.Minimized, connection.SafeDiagnostics);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (disposed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        input.SetActive(false);
        CompositionTarget.Rendering -= Render;
        presenter.Dispose();
        try { await connection.DisposeAsync(); }
        finally
        {
            SessionFiles.Write(directory, "exit.json", new { pid = Environment.ProcessId, renderedFrames, disposed = true });
            disposed = true; Close();
        }
    }
}
