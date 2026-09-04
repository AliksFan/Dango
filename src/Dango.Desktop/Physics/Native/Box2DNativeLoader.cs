using System.IO;
using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Physics.Native;

public readonly record struct Box2DNativeVersion(int Major, int Minor, int Revision)
{
    public override string ToString() => $"{Major}.{Minor}.{Revision}";
}

public sealed record Box2DNativeRequirements(int AbiVersion, Box2DNativeVersion Box2DVersion);

public enum Box2DNativeLoadFailureKind
{
    None,
    MissingLibrary,
    InvalidLibrary,
    UnsupportedArchitecture,
    MissingExport,
    AbiMismatch,
    VersionMismatch,
    LoadFailed,
}

public sealed record Box2DNativeLoadFailure(Box2DNativeLoadFailureKind Kind, string Message)
{
    public static Box2DNativeLoadFailure None { get; } = new(Box2DNativeLoadFailureKind.None, string.Empty);
}

public readonly record struct Box2DNativeProbeResult(
    bool Succeeded,
    int ErrorCode,
    int OutstandingBytes,
    string Message);

public static class Box2DNativeLoader
{
    public const string LibraryFileName = "dango_box2d.dll";

    public static Box2DNativeRequirements PinnedRequirements { get; } =
        new(5, new Box2DNativeVersion(3, 1, 1));

    public static string DefaultLibraryPath => Path.Combine(AppContext.BaseDirectory, LibraryFileName);

    public static bool TryLoad(
        string libraryPath,
        Box2DNativeRequirements requirements,
        out Box2DNativeSession? session,
        out Box2DNativeLoadFailure failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentNullException.ThrowIfNull(requirements);

        session = null;
        if (!File.Exists(libraryPath))
        {
            failure = new(
                Box2DNativeLoadFailureKind.MissingLibrary,
                $"Box2D native library is missing: {libraryPath}");
            return false;
        }

        var architecture = ReadPeArchitecture(libraryPath, out var architectureError);
        if (architecture is null)
        {
            failure = new(Box2DNativeLoadFailureKind.InvalidLibrary, architectureError!);
            return false;
        }
        if (architecture != PeArchitecture.Amd64 || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            failure = new(
                Box2DNativeLoadFailureKind.UnsupportedArchitecture,
                $"Box2D requires an x64 process and AMD64 DLL; process={RuntimeInformation.ProcessArchitecture}, library={architecture}.");
            return false;
        }

        nint handle = 0;
        try
        {
            handle = NativeLibrary.Load(Path.GetFullPath(libraryPath));
            if (!TryResolve(handle, "dango_box2d_get_abi_version", out GetIntegerDelegate getAbi) ||
                !TryResolve(handle, "dango_box2d_get_version_major", out GetIntegerDelegate getMajor) ||
                !TryResolve(handle, "dango_box2d_get_version_minor", out GetIntegerDelegate getMinor) ||
                !TryResolve(handle, "dango_box2d_get_version_revision", out GetIntegerDelegate getRevision) ||
                !TryResolve(handle, "dango_box2d_run_lifecycle_probe", out LifecycleProbeDelegate runProbe) ||
                !TryResolve(handle, "dango_box2d_soft_body_create", out SoftBodyCreateDelegate createSoftBody) ||
                !TryResolve(handle, "dango_box2d_soft_body_create_configured", out SoftBodyCreateConfiguredDelegate createConfiguredSoftBody) ||
                !TryResolve(handle, "dango_box2d_soft_body_step", out SoftBodyStepDelegate stepSoftBody) ||
                !TryResolve(handle, "dango_box2d_soft_body_destroy", out SoftBodyDestroyDelegate destroySoftBody))
            {
                failure = new(
                    Box2DNativeLoadFailureKind.MissingExport,
                    "Box2D native library does not expose the complete Dango ABI.");
                return false;
            }

            var actualAbi = getAbi();
            if (actualAbi != requirements.AbiVersion)
            {
                failure = new(
                    Box2DNativeLoadFailureKind.AbiMismatch,
                    $"Box2D bridge ABI mismatch: expected {requirements.AbiVersion}, got {actualAbi}.");
                return false;
            }

            var actualVersion = new Box2DNativeVersion(getMajor(), getMinor(), getRevision());
            if (actualVersion != requirements.Box2DVersion)
            {
                failure = new(
                    Box2DNativeLoadFailureKind.VersionMismatch,
                    $"Box2D version mismatch: expected {requirements.Box2DVersion}, got {actualVersion}.");
                return false;
            }

            session = new Box2DNativeSession(
                handle,
                actualVersion,
                runProbe,
                createSoftBody,
                createConfiguredSoftBody,
                stepSoftBody,
                destroySoftBody);
            handle = 0;
            failure = Box2DNativeLoadFailure.None;
            return true;
        }
        catch (BadImageFormatException exception)
        {
            failure = new(Box2DNativeLoadFailureKind.InvalidLibrary, exception.Message);
            return false;
        }
        catch (DllNotFoundException exception)
        {
            failure = new(Box2DNativeLoadFailureKind.LoadFailed, exception.Message);
            return false;
        }
        finally
        {
            if (handle != 0)
            {
                NativeLibrary.Free(handle);
            }
        }
    }

    private static bool TryResolve<TDelegate>(nint handle, string exportName, out TDelegate export)
        where TDelegate : Delegate
    {
        if (NativeLibrary.TryGetExport(handle, exportName, out var address))
        {
            export = Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
            return true;
        }

        export = null!;
        return false;
    }

    private static PeArchitecture? ReadPeArchitecture(string path, out string? error)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 70 || reader.ReadUInt16() != 0x5a4d)
            {
                error = "Native library is not a valid PE image.";
                return null;
            }

            stream.Position = 0x3c;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset > stream.Length - 6)
            {
                error = "Native library has an invalid PE header offset.";
                return null;
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                error = "Native library has an invalid PE signature.";
                return null;
            }

            var machine = reader.ReadUInt16();
            error = null;
            return machine switch
            {
                0x8664 => PeArchitecture.Amd64,
                0x014c => PeArchitecture.I386,
                0xaa64 => PeArchitecture.Arm64,
                _ => PeArchitecture.Unknown,
            };
        }
        catch (IOException exception)
        {
            error = $"Native library cannot be inspected: {exception.Message}";
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = $"Native library cannot be inspected: {exception.Message}";
            return null;
        }
    }

    private enum PeArchitecture
    {
        Unknown,
        I386,
        Amd64,
        Arm64,
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetIntegerDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int LifecycleProbeDelegate(int stepCount, out int outstandingBytes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SoftBodyCreateDelegate(int nodeCount, int substepCount, out nint softBodyHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SoftBodyCreateConfiguredDelegate(in NativeSoftBodyConfiguration configuration, out nint softBodyHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SoftBodyStepDelegate(
        nint softBodyHandle,
        float timeStep,
        int cue,
        float anchorX,
        float anchorY,
        float strength,
        float facing,
        int dragActive,
        float dragGrabX,
        float dragGrabY,
        [Out] float[] points,
        int pointCapacity,
        out int pointCount,
        out float signedArea,
        out float centroidX,
        out float centroidY,
        out float maximumRadius,
        out float maximumSpeed,
        out float energyProxy,
        out int sleeping,
        out float leftEyeX,
        out float leftEyeY,
        out float rightEyeX,
        out float rightEyeY,
        out float hatX,
        out float hatY,
        out float hatAngle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SoftBodyDestroyDelegate(nint softBodyHandle, out int outstandingBytes);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSoftBodyConfiguration
{
    public int NodeCount;
    public int SubStepCount;
    public float RestWidth;
    public float RestHeight;
    public float TopRoundness;
    public float LowerBulge;
    public float BottomFlatness;
    public float GroundCompression;
    public float DropHeight;
    public float ImpactSpeed;
    public float ReboundStrength;
    public float LiftOffLimit;
    public float SettleTime;
    public float MaximumCompression;
    public float LateralExpansion;
    public float RadialHertz;
    public float EdgeHertz;
    public float RadialDamping;
    public float EdgeDamping;
    public float LinearDamping;
    public float NodeMass;
    public float GroundHeight;
    public float NodeRadius;
    public float Friction;
    public float Restitution;
    public float ContactHertz;
    public float MaximumRadius;
    public float MaximumSpeed;
    public float MaximumCentroidOffset;
    public float MinimumArea;

    public static NativeSoftBodyConfiguration From(SoftBodyTuningParameters value) => new()
    {
        NodeCount = value.NodeCount,
        SubStepCount = value.SubStepCount,
        RestWidth = (float)value.RestWidth,
        RestHeight = (float)value.RestHeight,
        TopRoundness = (float)value.TopRoundness,
        LowerBulge = (float)value.LowerBulge,
        BottomFlatness = (float)value.BottomFlatness,
        GroundCompression = (float)value.GroundCompression,
        DropHeight = (float)value.DropHeight,
        ImpactSpeed = (float)value.ImpactSpeed,
        ReboundStrength = (float)value.ReboundStrength,
        LiftOffLimit = (float)value.LiftOffLimit,
        SettleTime = (float)value.SettleTime,
        MaximumCompression = (float)value.MaximumCompression,
        LateralExpansion = (float)value.LateralExpansion,
        RadialHertz = (float)value.RadialElasticity,
        EdgeHertz = (float)value.ContourElasticity,
        RadialDamping = (float)value.RadialDamping,
        EdgeDamping = (float)value.ContourDamping,
        LinearDamping = (float)value.OverallDamping,
        NodeMass = (float)value.NodeMass,
        GroundHeight = (float)value.GroundHeight,
        NodeRadius = (float)value.NodeRadius,
        Friction = (float)value.Friction,
        Restitution = (float)value.Restitution,
        ContactHertz = (float)value.ContactSoftness,
        MaximumRadius = (float)(value.MaximumDeformation * 1.4),
        MaximumSpeed = (float)value.MaximumSpeed,
        MaximumCentroidOffset = (float)(value.MaximumCentroidOffset * 2),
        MinimumArea = (float)value.MinimumContourArea,
    };
}

public sealed class Box2DNativeSession : IDisposable
{
    private nint _handle;
    private readonly Box2DNativeLoader.LifecycleProbeDelegate _runProbe;
    private readonly Box2DNativeLoader.SoftBodyCreateDelegate _createSoftBody;
    private readonly Box2DNativeLoader.SoftBodyCreateConfiguredDelegate _createConfiguredSoftBody;
    private readonly Box2DNativeLoader.SoftBodyStepDelegate _stepSoftBody;
    private readonly Box2DNativeLoader.SoftBodyDestroyDelegate _destroySoftBody;

    internal Box2DNativeSession(
        nint handle,
        Box2DNativeVersion box2DVersion,
        Box2DNativeLoader.LifecycleProbeDelegate runProbe,
        Box2DNativeLoader.SoftBodyCreateDelegate createSoftBody,
        Box2DNativeLoader.SoftBodyCreateConfiguredDelegate createConfiguredSoftBody,
        Box2DNativeLoader.SoftBodyStepDelegate stepSoftBody,
        Box2DNativeLoader.SoftBodyDestroyDelegate destroySoftBody)
    {
        _handle = handle;
        Box2DVersion = box2DVersion;
        _runProbe = runProbe;
        _createSoftBody = createSoftBody;
        _createConfiguredSoftBody = createConfiguredSoftBody;
        _stepSoftBody = stepSoftBody;
        _destroySoftBody = destroySoftBody;
    }

    public Box2DNativeVersion Box2DVersion { get; }

    ~Box2DNativeSession()
    {
        Dispose(false);
    }

    public Box2DNativeProbeResult RunLifecycleProbe(int stepCount)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        var errorCode = _runProbe(stepCount, out var outstandingBytes);
        return errorCode == 0
            ? new(true, 0, outstandingBytes, "Box2D create/step/destroy probe succeeded.")
            : new(
                false,
                errorCode,
                outstandingBytes,
                $"Box2D create/step/destroy probe failed with native code {errorCode} and {outstandingBytes} outstanding bytes.");
    }

    internal bool TryCreateSoftBody(
        int nodeCount,
        int substepCount,
        out Box2DSoftBodyNativeSession? softBody,
        out int errorCode)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        errorCode = _createSoftBody(nodeCount, substepCount, out var softBodyHandle);
        if (errorCode != 0 || softBodyHandle == 0)
        {
            softBody = null;
            return false;
        }

        softBody = new Box2DSoftBodyNativeSession(
            softBodyHandle,
            nodeCount,
            _stepSoftBody,
            _destroySoftBody);
        return true;
    }

    internal bool TryCreateSoftBody(
        SoftBodyTuningParameters parameters,
        out Box2DSoftBodyNativeSession? softBody,
        out int errorCode)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        var configuration = NativeSoftBodyConfiguration.From(parameters);
        errorCode = _createConfiguredSoftBody(in configuration, out var softBodyHandle);
        if (errorCode != 0 || softBodyHandle == 0)
        {
            softBody = null;
            return false;
        }
        softBody = new Box2DSoftBodyNativeSession(softBodyHandle, parameters.NodeCount, _stepSoftBody, _destroySoftBody);
        return true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
        {
            NativeLibrary.Free(handle);
        }
    }
}

internal readonly record struct Box2DSoftBodyNativeFrame(
    bool Succeeded,
    int ErrorCode,
    float[] Points,
    int PointCount,
    float SignedArea,
    float CentroidX,
    float CentroidY,
    float MaximumRadius,
    float MaximumSpeed,
    float EnergyProxy,
    bool IsSleeping,
    float LeftEyeX,
    float LeftEyeY,
    float RightEyeX,
    float RightEyeY,
    float HatX,
    float HatY,
    float HatAngle);

internal sealed class Box2DSoftBodyNativeSession : IDisposable
{
    private nint _handle;
    private readonly int _nodeCount;
    private readonly Box2DNativeLoader.SoftBodyStepDelegate _step;
    private readonly Box2DNativeLoader.SoftBodyDestroyDelegate _destroy;

    public Box2DSoftBodyNativeSession(
        nint handle,
        int nodeCount,
        Box2DNativeLoader.SoftBodyStepDelegate step,
        Box2DNativeLoader.SoftBodyDestroyDelegate destroy)
    {
        _handle = handle;
        _nodeCount = nodeCount;
        _step = step;
        _destroy = destroy;
    }

    public Box2DSoftBodyNativeFrame Step(
        TimeSpan fixedStep,
        int cue,
        float anchorX,
        float anchorY,
        float strength,
        float facing,
        bool dragActive,
        float dragGrabX,
        float dragGrabY)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        var points = new float[_nodeCount * 2];
        var errorCode = _step(
            _handle,
            (float)fixedStep.TotalSeconds,
            cue,
            anchorX,
            anchorY,
            strength,
            facing,
            dragActive ? 1 : 0,
            dragGrabX,
            dragGrabY,
            points,
            _nodeCount,
            out var pointCount,
            out var signedArea,
            out var centroidX,
            out var centroidY,
            out var maximumRadius,
            out var maximumSpeed,
            out var energyProxy,
            out var sleeping,
            out var leftEyeX,
            out var leftEyeY,
            out var rightEyeX,
            out var rightEyeY,
            out var hatX,
            out var hatY,
            out var hatAngle);
        return new(
            errorCode == 0,
            errorCode,
            points,
            pointCount,
            signedArea,
            centroidX,
            centroidY,
            maximumRadius,
            maximumSpeed,
            energyProxy,
            sleeping != 0,
            leftEyeX,
            leftEyeY,
            rightEyeX,
            rightEyeY,
            hatX,
            hatY,
            hatAngle);
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
        {
            var errorCode = _destroy(handle, out var outstandingBytes);
            if (errorCode != 0 || outstandingBytes != 0)
            {
                throw new InvalidOperationException(
                    $"Box2D soft-body cleanup failed with native code {errorCode} and {outstandingBytes} outstanding bytes.");
            }
        }

        GC.SuppressFinalize(this);
    }
}
