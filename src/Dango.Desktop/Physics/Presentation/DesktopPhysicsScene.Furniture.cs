using Dango.Core.Coordinates;
using Dango.Core.Furniture;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation;

public sealed partial class DesktopPhysicsScene
{
    // The ordinary-room coordinator borrows this world's operations. It never
    // creates, advances, resets, or disposes the world's owner.
    internal bool SupportsLiveFurnitureEditing
    {
        get { VerifyAccess(); return world.SupportsLiveFurnitureEditing; }
    }

    internal DangoFurnitureFrame ReadFurnitureFrame()
    { VerifyAccess(); return world.ReadFurnitureFrame(); }

    internal DangoStaticPlatformProbe ProbeStaticPlatformChange(DangoStaticPlatformChangeKind kind,
        ulong instance, DangoStaticPlatformDefinition? candidate, ulong revision)
    { VerifyAccess(); return world.ProbeStaticPlatformChange(kind, instance, candidate, revision); }

    internal DangoStaticPlatformPreparation PrepareStaticPlatformChange(DangoStaticPlatformChangeKind kind,
        ulong instance, DangoStaticPlatformDefinition? candidate, ulong revision)
    { VerifyAccess(); return world.PrepareStaticPlatformChange(kind, instance, candidate, revision); }

    internal DangoFurnitureFrame ApplyPreparedStaticPlatformChange(DangoPreparedStaticPlatformChange token)
    { VerifyAccess(); return world.ApplyPreparedStaticPlatformChange(token); }

    internal void CancelPreparedStaticPlatformChange(DangoPreparedStaticPlatformChange token)
    { VerifyAccess(); world.CancelPreparedStaticPlatformChange(token); }

    internal DangoStaticPlatformDefinition OrdinaryFurnitureDefinition(FurniturePlatformInstance item)
    {
        VerifyAccess();
        item.Validate(FurnitureLayoutContract.OrdinaryRoom);
        var top = ToWorld(new LogicalPoint(item.Anchor.X, GroundY - item.Anchor.Y));
        return new(top.X, top.Y - .35 / 2, 3.4, .35, .72);
    }
}
