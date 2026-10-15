using System.Diagnostics;
using System.Globalization;
using System.IO;
using Dango.Desktop.Physics.Presentation;

namespace Dango.ShapeReview;

internal sealed record DynamicFrame(int Number, bool Failed, bool Sleeping, int Awake, int Stable, double Speed, double X, double Y, int Selected, PresentationPoint[] Nodes);

internal sealed class DynamicSession : IAsyncDisposable
{
    private readonly Process process;
    private readonly SemaphoreSlim gate = new(1);
    private bool disposed;
    public int ProcessId => process.Id;

    private DynamicSession(Process process) => this.process = process;

    public static async Task<DynamicSession> Start(string executable, bool test = false)
    {
        var info = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(test ? "interactive-review-test" : "interactive-review");
        var process = Process.Start(info) ?? throw new IOException("无法启动隔离物理进程。");
        // Read and discard diagnostic stderr continuously; never let a full pipe stall the owned process.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        var result = new DynamicSession(process);
        try
        {
            if (await result.Read() != "ready 1")
            {
                throw new IOException("原生进程握手失败。");
            }
            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
    }

    private async Task<string> Read()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
        return line is not null && line.Length <= 4096 ? line : throw new IOException("原生响应缺失或过长。");
    }

    public async Task<string> Request(string command)
    {
        await gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync();
            return await Read();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DynamicFrame> Step(double horizontal = 0, double down = 0, bool jump = false, bool drag = false, double x = 0, double y = 0) =>
        Parse(await Request(FormattableString.Invariant($"step {horizontal:R} {down:R} {(jump ? 1 : 0)} {(drag ? 1 : 0)} {x:R} {y:R}")));

    public static DynamicFrame Parse(string response)
    {
        var tokens = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 42 || tokens[0] != "frame")
        {
            throw new InvalidDataException("无效原生帧。");
        }
        var values = tokens.Skip(1).Select(token => double.Parse(token, CultureInfo.InvariantCulture)).ToArray();
        if (values.Any(value => !double.IsFinite(value)) || values[0] is < 0 or > int.MaxValue || values[3] is < 0 or > 17 ||
            values[1] is not (0 or 1) || values[2] is not (0 or 1) || values[8] is < -1 or > 15 ||
            new[] { 0, 1, 2, 3, 4, 8 }.Any(index => values[index] != Math.Truncate(values[index])))
        {
            throw new InvalidDataException("原生帧数值非法。");
        }
        return new((int)values[0], values[1] != 0, values[2] != 0, (int)values[3], (int)values[4], values[5], values[6], values[7], (int)values[8],
            Enumerable.Range(0, 16).Select(i => new PresentationPoint(values[9 + i * 2], values[10 + i * 2])).ToArray());
    }

    public async Task Reset()
    {
        if (await Request("reset") != "ready 1")
        {
            throw new IOException("显式重置失败。");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    // Only terminate this instance's explicitly owned child, never a name-based process group.
                    process.Kill();
                    await process.WaitForExitAsync();
                }
            }
            process.Dispose();
        }
        finally
        {
            gate.Release();
        }
    }
}
