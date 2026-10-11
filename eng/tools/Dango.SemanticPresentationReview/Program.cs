using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;

namespace Dango.SemanticPresentationReview;

internal sealed class NativeAllocation : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AllocatedBytes();
    private readonly nint module;
    private readonly AllocatedBytes read;
    internal NativeAllocation(string library)
    {
        module = NativeLibrary.Load(library);
        read = Marshal.GetDelegateForFunctionPointer<AllocatedBytes>(NativeLibrary.GetExport(module, "dango_physics_get_allocated_bytes"));
    }
    internal int Read() => read();
    public void Dispose() => NativeLibrary.Free(module);
}

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 5 or > 6 || args[0] is not ("--verify" or "--verify-push" or "--verify-stack" or "--verify-withdraw" or "--review" or "--window-check")) return 2;
        var verification = args[0].StartsWith("--verify", StringComparison.Ordinal);
        var output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) return 2;
        Directory.CreateDirectory(output);
        var mutation = args.Length == 6 ? args[5] : "none";
        if (mutation is not ("none" or "wrong-origin" or "shared-replica" or "fallback" or "stale-visual")) return 2;
        ReviewInputs? inputs = null; NativeAllocation? native = null; Dictionary<string, string>? afterHashes = null;
        var before = -1; var after = -1; Exception? failure = null; var cases = new List<object>(); var exit = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            inputs = new(args[1], args[2], args[3]); native = new(inputs.Library); before = native.Read();
            Write(Path.Combine(output, "inputs.json"), new { Pid = Environment.ProcessId, mode = args[0], mutation, inputs.BeforeHashes, allocationBefore = before });
            if (verification)
            {
                var scenarios = args[0] == "--verify" ? new[] { "push", "stack", "withdraw" } : [args[0][9..]];
                foreach (var scenario in scenarios)
                    foreach (var rates in new[] { (30, 60), (60, 144), (144, 30) })
                    {
                        using var surface = new ReviewSurface(inputs, scenario, mutation);
                        surface.Layout(1200, 760);
                        var rendersA = 0; var rendersB = 0;
                        for (var time = 1; time <= 7200; time++)
                        {
                            var tick = Math.Min(600, time / 12);
                            if (time % (720 / rates.Item1) == 0) { surface.Left.AdvanceTo(tick); rendersA++; }
                            if (time % (720 / rates.Item2) == 0) { surface.Right.AdvanceTo(tick); rendersB++; }
                            surface.CheckCommonTime();
                            if (time == 1800)
                            {
                                foreach (var layout in new[] { (960, 620), (1200, 760), (960, 1100) })
                                {
                                    surface.Layout(layout.Item1, layout.Item2);
                                    surface.SaveImage(Path.Combine(output, $"{scenario}-{rates.Item1}-{rates.Item2}-{layout.Item1}x{layout.Item2}.png"));
                                }
                                surface.Layout(1200, 760);
                            }
                        }
                        if (surface.Left.Steps != 600 || surface.Right.Steps != 600 || surface.CommonTimeChecks == 0)
                            throw new GateFailure("full-trajectory");
                        if (scenario != "push" && (surface.Left.SupportedSteps <= 30 || surface.Right.SupportedSteps <= 30))
                            throw new GateFailure("missing-real-support");
                        if (scenario == "withdraw" && (surface.Left.RemovedSteps != 420 || surface.Right.RemovedSteps != 420))
                            throw new GateFailure("withdrawal-trajectory");
                        cases.Add(new { scenario, rateA = rates.Item1, rateB = rates.Item2, rendersA, rendersB,
                            surface.CommonTimeChecks, surface.MaximumCommonCenterError, left = surface.Left.Evidence(), right = surface.Right.Evidence() });
                        Write(Path.Combine(output, "progress.json"), cases);
                    }
                exit = 0;
            }
            else
            {
                var window = new ReviewWindow(inputs, output, args[0] == "--window-check"); app.Run(window);
                exit = window.ExitCode;
            }
        }
        catch (Exception error) { failure = error; Console.Error.WriteLine(error); }
        finally
        {
            try
            {
                after = native?.Read() ?? -1; afterHashes = inputs?.Recheck();
                if (before >= 0 && before != after) { failure ??= new GateFailure("native-allocation-returned"); exit = 1; }
            }
            catch (Exception error) { failure ??= error; exit = 1; }
            native?.Dispose();
            Write(Path.Combine(output, "results.json"), new
            {
                Version = 1, Mode = args[0], Mutation = mutation, Pass = exit == 0 && failure is null, ExitCode = exit,
                Pid = Environment.ProcessId, Oracle = (failure as GateFailure)?.Oracle, Error = failure?.ToString(),
                beforeHashes = inputs?.BeforeHashes, afterHashes, allocationBefore = before, allocationAfter = after, cases,
                Scope = verification ? "Actual WPF offscreen geometry; no foreground, network, formal server or multiplayer acceptance." :
                    "Isolated visible WPF semantic replay; no network, formal server, account, database or dual-machine acceptance.",
            });
        }
        return exit;
    }
}
