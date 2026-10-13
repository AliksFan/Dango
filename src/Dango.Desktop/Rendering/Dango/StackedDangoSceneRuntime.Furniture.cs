using System.IO;
using Dango.Core.Coordinates;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Physics.Configuration;

namespace Dango.Desktop.Rendering.Visuals;

public sealed partial class StackedDangoSceneRuntime
{
    private string? ordinaryFurnitureDataRoot;
    private bool ordinaryFurnitureGlove;
    private LogicalPoint gloveTargetNative, gloveRequestedNative;
    internal OrdinaryRoomFurnitureSession? Furniture { get; private set; }

    internal void ConfigureOrdinaryFurniture(string dataRoot)
    {
        VerifyAccess();
        var normalized = Path.GetFullPath(dataRoot);
        if (ordinaryFurnitureDataRoot is not null && ordinaryFurnitureDataRoot != normalized)
            throw new InvalidOperationException("本地普通房家具路径已绑定，不能换成另一房间。");
        ordinaryFurnitureDataRoot = normalized;
        EnsureOrdinaryFurniture();
    }

    private void EnsureOrdinaryFurniture()
    {
        if (Furniture is not null || ordinaryFurnitureDataRoot is null || scene is null) return;
        Furniture = new(scene, ordinaryFurnitureDataRoot, ownsActiveRoom: () =>
            FrameDriver is not null && running && LastError is null && !unboundedHorizontalPreview && scene.Count == 1);
    }

    internal bool AttachOrdinaryFurniture()
    {
        VerifyAccess();
        EnsureOrdinaryFurniture();
        if (Furniture is null || scene is null || FrameDriver is null || !running || LastError is not null ||
            unboundedHorizontalPreview || localHorizontalBounds is null) return false;
        var attached = Furniture.Attach();
        if (!attached && !Furniture.CanStep)
        {
            LastError = Furniture.Status;
            ClearPhysicalInput();
        }
        return attached;
    }

    internal bool TryDetachOrdinaryFurniture()
    {
        VerifyAccess();
        if (Furniture is null) return true;
        if (!Furniture.TryDetach()) return false;
        EndOrdinaryFurnitureGlove();
        return true;
    }

    internal bool BeginOrdinaryFurnitureGlove(double roomX, double height)
    {
        VerifyAccess();
        if (scene is null || Furniture is not { CanEdit: true } || LastPhysicsFrame?.Bodies.SingleOrDefault() is not { } body)
            return false;
        gloveRequestedNative = ToGloveNative(roomX, height);
        // Begin at the actual highest node, then move the requested target at
        // the accepted rate. A far pointer cannot inject an initial target jump.
        var node = Enumerable.Range(0, body.PointCount).MaxBy(index => body.Contour[index * 2 + 1]);
        var x = (double)body.Contour[node * 2];
        var y = (double)body.Contour[node * 2 + 1];
        var length = Math.Sqrt(x * x + y * y);
        var nodeRadius = scene.ConfigurationOf(body.EntityId).NodeRadius;
        var ratio = length > nodeRadius ? 1 - nodeRadius / length : 1;
        gloveTargetNative = new(body.CoreX + x * ratio, body.CoreY + y * ratio);
        BeginPhysicalDrag(scene.SelectedEntity, scene.ToHost(gloveTargetNative));
        ordinaryFurnitureGlove = true;
        return true;
    }

    internal void UpdateOrdinaryFurnitureGlove(double roomX, double height)
    {
        VerifyAccess();
        if (!ordinaryFurnitureGlove || scene is null || Furniture is not { IsAttached: true, CanStep: true }) return;
        gloveRequestedNative = ToGloveNative(roomX, height);
    }

    internal void EndOrdinaryFurnitureGlove()
    {
        if (disposed) return;
        VerifyAccess();
        if (!ordinaryFurnitureGlove) return;
        ordinaryFurnitureGlove = false;
        EndPhysicalDrag();
    }

    private LogicalPoint ToGloveNative(double roomX, double height)
    {
        if (!double.IsFinite(roomX) || !double.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(roomX));
        var contract = FurnitureLayoutContract.OrdinaryRoom;
        var x = Math.Clamp(roomX, contract.Left + 64, contract.Right - 64);
        var y = Math.Clamp(height, 48, contract.Ceiling - 48);
        return scene!.ToWorld(new(x, scene.GroundY - y));
    }

    private bool ProcessOrdinaryFurnitureBoundary(TimeSpan fixedStep)
    {
        Furniture?.ProcessBoundary();
        if (Furniture is { IsAttached: true, CanStep: false })
        {
            LastError = Furniture.Status;
            ClearPhysicalInput();
            return false;
        }
        if (ordinaryFurnitureGlove && IsDragging && scene is not null)
        {
            var dx = gloveRequestedNative.X - gloveTargetNative.X;
            var dy = gloveRequestedNative.Y - gloveTargetNative.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var maximum = 8 * fixedStep.TotalSeconds;
            gloveTargetNative = distance <= maximum || distance == 0 ? gloveRequestedNative :
                new(gloveTargetNative.X + dx * maximum / distance, gloveTargetNative.Y + dy * maximum / distance);
            scene.MoveDrag(scene.ToHost(gloveTargetNative));
        }
        return true;
    }

    private bool OrdinaryFurnitureAllowsRehost(LogicalPoint referenceCenter)
    {
        if (Furniture is not { IsAttached: true } || hostOrigin == referenceCenter) return true;
        Furniture.FailRuntime("普通房坐标绑定尚未卸载，禁止自动换场。");
        LastError = Furniture.Status;
        ClearPhysicalInput();
        return false;
    }
}
