using System.IO;

namespace Dango.ShapeReview;

internal static class DynamicVerification
{
    public static DynamicFrame? VerifiedFrame { get; private set; }

    public static async Task Run(string executable, string output)
    {
        Directory.CreateDirectory(output);
        using var log = new StreamWriter(Path.Combine(output, "dynamic-verification.txt")) { AutoFlush = true };
        static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
        await using var session = await DynamicSession.Start(executable, true);
        log.WriteLine($"Owned native PID={session.ProcessId}");
        foreach (var invalid in new[] { "step NaN 0 0 0 0 0", "step 2 0 0 0 0 0", "step 0", "step 0 0 4294967296 0 0 0", "step 0 0 .5 0 0 0", new string('x', 300) })
        {
            Check(await session.Request(invalid) == "error invalid", "Invalid input was not rejected");
        }
        var frame = await session.Step();
        Check(frame.Number == 1, "Invalid input advanced simulation");
        log.WriteLine("PASS invalid messages rejected without advancing");
        async Task<DynamicFrame> Settle()
        {
            for (var i = 0; i < 6000; i++)
            {
                var next = await session.Step();
                Check(!next.Failed, $"Native failure frame={next.Number}");
                if (next.Sleeping)
                {
                    Check(next.Awake == 0, "Sleep UI must reflect all native bodies asleep");
                    return next;
                }
            }
            throw new InvalidDataException("No sleep within 6000 frames");
        }
        frame = await Settle();
        var initial = frame;
        VerifiedFrame = initial;
        for (var i = 0; i < 120; i++)
        {
            frame = await session.Step();
            Check(frame.Sleeping && frame.Awake == 0 && frame.X == initial.X && frame.Y == initial.Y && frame.Nodes.SequenceEqual(initial.Nodes), "Sleeping state changed");
        }
        log.WriteLine($"PASS initial sleep frame={initial.Number} and 120 exact unchanged frames");
        foreach (var horizontal in new[] { 1d, -1d })
        {
            var before = frame.X;
            for (var i = 0; i < 30; i++)
            {
                frame = await session.Step(horizontal);
                Check(!frame.Failed && !frame.Sleeping, "Horizontal input did not wake");
            }
            Check((frame.X - before) * horizontal > .01, "Horizontal direction failed");
            frame = await Settle();
            log.WriteLine($"PASS horizontal={horizontal} wake, move, release and sleep");
        }
        var baselineY = frame.Y;
        frame = await session.Step(jump: true);
        var peak = frame.Y;
        for (var i = 0; i < 120; i++)
        {
            frame = await session.Step();
            Check(!frame.Failed, "Jump failed");
            peak = Math.Max(peak, frame.Y);
        }
        Check(peak - baselineY > .02, "No real jump");
        frame = await Settle();
        log.WriteLine($"PASS jump height={peak - baselineY:R} and landing sleep");
        baselineY = frame.Y;
        for (var i = 0; i < 120; i++)
        {
            frame = await session.Step(down: 1);
            Check(!frame.Failed, "Downward input failed");
        }
        Check(baselineY - frame.Y > .01, "No real compression");
        frame = await Settle();
        log.WriteLine("PASS down compression and release sleep");
        var beforeDrag = frame.X;
        var highest = Enumerable.Range(0, 16).MaxBy(index => frame.Nodes[index].Y);
        for (var i = 0; i < 120; i++)
        {
            frame = await session.Step(drag: true, x: beforeDrag + .6, y: .4);
            Check(!frame.Failed && !frame.Sleeping, "Drag failed");
            Check(frame.Selected == highest, "Drag did not preserve initially highest selected node");
        }
        Check(frame.X - beforeDrag > .1, "Explicit pointer target ignored");
        frame = await Settle();
        Check(frame.Selected == -1, "Drag selection was not released");
        log.WriteLine("PASS highest-node selection, explicit drag target, release and sleep");
        var fault = DynamicSession.Parse(await session.Request("fault"));
        var latched = await session.Step(horizontal: 1);
        Check(fault.Failed && latched.Failed && fault.Number == latched.Number, "Failure not latched");
        await session.Reset();
        frame = await session.Step();
        Check(frame.Number == 1 && !frame.Failed, "Explicit reset failed");
        Check(await session.Request("quit") == "bye 0", "World resources not released");
        log.WriteLine("PASS failure latch, explicit reset and zero-allocation exit");
        log.WriteLine("PASS process-level dynamic sequence; human input/feel still pending");
    }
}
