using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

if (args.Length != 1) throw new ArgumentException("Expected a new output JSON file path.");
var output = Path.GetFullPath(args[0]);
if (File.Exists(output)) throw new IOException("Output already exists.");
var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
foreach (var protectedRoot in new[] { Path.Combine(local, "Dango"), Path.Combine(local, "Programs", "Dango") })
    if (output.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Output must be outside daily settings and installation directories.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

var oval = Shape(0.7, 0.8, 0);
var sheared = Shape(0.7, 0.6, 0.16);
var narrow = Shape(0.25, 0.8, 0.05);
var root = new PresentationAttachment(new(0.04, 0.75), 0.2);
var placement = HatPlacementSolver.Create(oval, root) ?? throw new InvalidOperationException("Invalid fixture.");
var normal = new EyeMotionSample(new(0.4, -0.2), 1, EyeShape.Round, 0);
var closed = new EyeMotionSample(new(0, 0), 0.25, EyeShape.Sleeping, 1);
var cases = new (string Name, int Count, Func<double> Run)[]
{
    ("hat-create-oval", 128, () => Pose(HatPlacementSolver.Create(oval, root))),
    ("hat-remap-sheared", 128, () => Pose(placement.Remap(sheared))),
    ("hat-available-lift", 8192, () => placement.AvailableLift(0)),
    ("face-oval-round", 256, () => Face(FaceLayoutSolver.Solve(oval, normal, normal, 0))),
    ("face-narrow-asymmetric", 128, () => Face(FaceLayoutSolver.Solve(narrow, normal, closed, 0.08))),
};
var records = new List<object>();
double checksum = 0;
// Fixed warmup and rotating case order are identical in baseline and candidate.
foreach (var item in cases)
    for (var i = 0; i < 256; i++) checksum += item.Run();
for (var round = 0; round < 7; round++)
    for (var offset = 0; offset < cases.Length; offset++)
    {
        var item = cases[(round + offset) % cases.Length];
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        double sum = 0;
        for (var i = 0; i < item.Count; i++) sum += item.Run();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        checksum += sum;
        records.Add(new
        {
            round,
            item.Name,
            iterations = item.Count,
            elapsedMs = elapsed,
            nanosecondsPerCall = elapsed * 1_000_000 / item.Count,
            allocatedBytes = allocated,
            bytesPerCall = (double)allocated / item.Count,
            checksum = sum
        });
    }
var result = new
{
    schema = 1,
    utc = DateTimeOffset.UtcNow,
    purpose = "Local geometry comparison only; not process CPU, live memory, FPS or host acceptance.",
    framework = RuntimeInformation.FrameworkDescription,
    processorCount = Environment.ProcessorCount,
    stopwatchFrequency = Stopwatch.Frequency,
    geometryAssembly = Identity(typeof(HatPlacementSolver).Assembly),
    coreAssembly = Identity(typeof(EyeShape).Assembly),
    probeAssembly = Identity(Assembly.GetExecutingAssembly()),
    warmupCallsPerCase = 256,
    measuredRounds = 7,
    checksum,
    records
};
using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine($"Wrote {records.Count} measurements; checksum={checksum:R}");

static object Identity(Assembly assembly) => new
{
    name = assembly.GetName().Name,
    version = assembly.GetName().Version?.ToString(),
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))
};
static AttachmentShapeGeometry Shape(double width, double height, double shear) =>
    AttachmentShapeGeometry.Create(Enumerable.Range(0, 16).Select(i =>
    {
        var angle = i * Math.PI / 8; var y = height * Math.Sin(angle);
        return new PresentationPoint(width * Math.Cos(angle) + shear * y, y);
    }).ToArray());
static double Pose(HatPlacementSolver? solver) => solver is null ? -1 : solver.Minimum + solver.Maximum;
static double Face(FaceLayout layout) => layout.Left.Center.X + layout.Right.Center.Y +
    layout.GazeScale + layout.SpacingScale + layout.SizeScale;
