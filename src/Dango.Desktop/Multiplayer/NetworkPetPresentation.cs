using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Physics.Configuration;
using Dango.Protocol;

namespace Dango.Desktop.Multiplayer;

internal interface INetworkPhysicsWorld : IDisposable
{
    DangoBodyState Step(TimeSpan fixedStep);
}

internal sealed class NetworkNativeWorld : INetworkPhysicsWorld
{
    private readonly DangoPhysicsWorld world;
    private readonly DangoEntityId entity;
    public NetworkNativeWorld(string path)
    {
        world = DangoPhysicsWorld.Create(path, capacity: 1);
        try { entity = world.Add(0, 0); }
        catch { world.Dispose(); throw; }
    }
    public DangoBodyState Step(TimeSpan fixedStep)
    {
        var frame = world.Step(fixedStep, [new(entity)]);
        if (!frame.Succeeded) throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure,
            "Network presentation failed.", frame.ErrorCode);
        if (frame.Bodies.Count != 1 || frame.ContactStepCount != 0)
            throw new InvalidOperationException("Network presentation requires one isolated body.");
        return frame.Bodies[0];
    }
    public void Dispose() => world.Dispose();
}

/// <summary>One native world and independent eye/hat history per network pet, owned by the UI thread.</summary>
public sealed class NetworkPetPresentation : IDisposable
{
    private readonly string petId;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly DangoPhysicsPresentationAdapter adapter = new(DangoPhysicsDefaults.Current);
    private readonly AttachmentCommandRouter attachments = new(enableHatMotion: true);
    private readonly AttachmentMotionController attachmentMotion;
    private INetworkPhysicsWorld? world;
    private PhysicsPresentationFallbackReason failure;
    private long frameNumber;
    private bool paused;
    private bool disposed;

    public NetworkPetPresentation(string petId, string nativeLibraryPath)
        : this(petId, () => new NetworkNativeWorld(nativeLibraryPath)) { }

    internal NetworkPetPresentation(string petId, Func<INetworkPhysicsWorld> worldFactory)
    {
        if (!RoomProtocol.IsId(petId)) throw new ArgumentException("Invalid pet ID.", nameof(petId));
        this.petId = petId;
        attachmentMotion = new(attachments, attachments.Add(new(1)));
        try { world = worldFactory(); }
        catch (Exception error) when (IsPresentationFailure(error))
        { failure = PhysicsPresentationFallbackReason.NativeUnavailable; }
    }

    public NetworkPetFrame Sample(PetState pet, long serverTimeMs, LogicalRect viewport,
        TimeSpan fixedStep, bool reducedMotion = false)
    {
        VerifyAccess();
        if (paused) throw new InvalidOperationException("Presentation is paused; do not catch up hidden frames.");
        if (!RoomProtocol.IsPet(pet) || pet.PetId != petId) throw new ArgumentException("Invalid pet state.", nameof(pet));
        if (serverTimeMs < 0 || fixedStep <= TimeSpan.Zero || fixedStep > TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        ValidateViewport(viewport);
        var sampled = RoomSampling.At(pet, serverTimeMs);
        var facing = sampled.Facing switch { "left" => DangoFacingDirection.Left, "right" => DangoFacingDirection.Right, _ => DangoFacingDirection.Center };
        PhysicsPresentationFrame? presentation = null;
        if (world is not null && !reducedMotion)
        {
            try
            {
                var native = world.Step(fixedStep);
                presentation = adapter.Present(native, fixedStep, facing);
                if (!DangoStreamGeometryFactory.TryCreate(presentation.Contour, 160, 120).Succeeded)
                    throw new InvalidOperationException("Native contour cannot be safely drawn.");
                presentation = presentation with { Attachments = attachmentMotion.Update(presentation,
                    frameNumber, fixedStep.TotalSeconds, facing) };
            }
            catch (Exception error) when (IsPresentationFailure(error))
            {
                failure = PhysicsPresentationFallbackReason.SimulationUnstable;
                presentation = null;
                ReleaseWorld();
            }
        }
        presentation ??= AuthoredPoseSampler.CreateFallback(new(petId, new(0, 0, 160, 120),
            DangoActionState.Idle, sampled.Action?.StartedAtServerTimeMs ?? 0,
            sampled.Action?.VariantSeed ?? 0, "room-ground", new(0, -1), 0, 1, reducedMotion,
            true, [], facingDirection: facing),
            reducedMotion ? PhysicsPresentationFallbackReason.ReducedMotion : failure);
        // The embedded fallback body is the renderer's fixed authored contour (not a native shape).
        var geometry = DangoStreamGeometryFactory.TryCreate(presentation.FallbackReason == PhysicsPresentationFallbackReason.None
            ? presentation.Contour : FallbackContour, 160, 120).Geometry!;
        var bottom = geometry.Bounds.Bottom;
        var anchorX = viewport.X + 80 + sampled.AnchorPosition.X * (viewport.Width - 160);
        var anchorY = viewport.Bottom - 8 - sampled.AnchorPosition.Y * (viewport.Height - 144);
        var wave = WavePresentationSampler.Sample(sampled.Action, serverTimeMs, reducedMotion);
        var strain = MovementStrain(sampled, serverTimeMs, reducedMotion);
        return new(petId, sampled.AnchorPosition, new(anchorX - 80, anchorY - bottom, 160, 120),
            presentation, wave, bottom, wave.IsActive ? wave.ScaleX : 1 - strain / 2,
            wave.IsActive ? wave.ScaleY : 1 + strain, ++frameNumber);
    }

    internal static IReadOnlyList<PresentationPoint> FallbackContour { get; } = Enumerable.Range(0, 12)
        .Select(i => 2 * Math.PI * i / 12)
        .Select(angle => new PresentationPoint(.72 * Math.Cos(angle), .82 * Math.Sin(angle))).ToArray();

    private static double MovementStrain(PetState pet, long serverTimeMs, bool reduced)
    {
        if (pet.Motion is not { } motion || serverTimeMs < motion.StartedAtServerTimeMs || reduced) return 0;
        var start = Math.Clamp((serverTimeMs - motion.StartedAtServerTimeMs) / 100d, 0, 1);
        var end = Math.Clamp((motion.EndsAtServerTimeMs - serverTimeMs) / 100d, 0, 1);
        // A spatial phase reads confirmed movement only, never keyboard or local physics velocity.
        return .004 * Math.Sin(2 * Math.PI * pet.AnchorPosition.X * 24) * Math.Min(start, end);
    }

    public void SetPaused(bool value)
    {
        VerifyAccess(); paused = value; attachmentMotion.Paused = value; attachments.SetPaused(value);
    }
    private static void ValidateViewport(LogicalRect viewport)
    {
        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) || !double.IsFinite(viewport.Width) ||
            !double.IsFinite(viewport.Height) || viewport.Width < 176 || viewport.Height < 160)
            throw new ArgumentOutOfRangeException(nameof(viewport), "A safe reference viewport of at least 176 x 160 DIP is required.");
    }
    private static bool IsPresentationFailure(Exception error) => error is DangoPhysicsException or
        ArgumentException or InvalidOperationException or ArithmeticException;
    private void ReleaseWorld()
    {
        var current = world; world = null;
        try { current?.Dispose(); }
        catch (DangoPhysicsException) { failure = PhysicsPresentationFallbackReason.SimulationUnstable; }
    }
    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Network presentation must remain on its owner thread.");
    }
    public void Dispose()
    {
        if (disposed) return;
        VerifyAccess(); ReleaseWorld(); attachments.Dispose(); disposed = true;
    }
}
