using System.IO;
using Dango.Core.Coordinates;
using Dango.Core.Furniture;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation;

public sealed partial class DesktopPhysicsScene
{
    private void RequireLegacyFurniture()
    { if (beltRoom is not null) throw new NotSupportedException("Belt room furniture editing is not yet supported."); }

    // The ordinary-room coordinator borrows this world's operations. It never
    // creates, advances, resets, or disposes the world's owner.
    internal bool SupportsLiveFurnitureEditing
    {
        get { VerifyAccess(); return beltRoom is null && world.SupportsLiveFurnitureEditing; }
    }

    internal DangoFurnitureFrame ReadFurnitureFrame()
    { VerifyAccess(); RequireLegacyFurniture(); return world.ReadFurnitureFrame(); }

    internal DangoStaticPlatformProbe ProbeStaticPlatformChange(DangoStaticPlatformChangeKind kind,
        ulong instance, DangoStaticPlatformDefinition? candidate, ulong revision)
    { VerifyAccess(); RequireLegacyFurniture(); return world.ProbeStaticPlatformChange(kind, instance, candidate, revision); }

    internal DangoStaticPlatformPreparation PrepareStaticPlatformChange(DangoStaticPlatformChangeKind kind,
        ulong instance, DangoStaticPlatformDefinition? candidate, ulong revision)
    { VerifyAccess(); RequireLegacyFurniture(); return world.PrepareStaticPlatformChange(kind, instance, candidate, revision); }

    internal DangoFurnitureFrame ApplyPreparedStaticPlatformChange(DangoPreparedStaticPlatformChange token)
    { VerifyAccess(); RequireLegacyFurniture(); return world.ApplyPreparedStaticPlatformChange(token); }

    internal void CancelPreparedStaticPlatformChange(DangoPreparedStaticPlatformChange token)
    { VerifyAccess(); RequireLegacyFurniture(); world.CancelPreparedStaticPlatformChange(token); }

    internal bool SupportsFurnitureCollection
    { get { VerifyAccess(); return beltRoom is null && world.SupportsFurnitureCollection && world.RoundBallGravityScale == .5; } }

    internal DangoFurnitureCollectionFrame ReadFurnitureCollectionFrame()
    { VerifyAccess(); RequireLegacyFurniture(); return world.ReadFurnitureCollectionFrame(); }

    internal DangoFurnitureProbe ProbeFurnitureChange(DangoFurnitureChangeKind kind,
        ulong instance, DangoFurnitureDefinition? candidate, ulong revision)
    { VerifyAccess(); RequireLegacyFurniture(); return world.ProbeFurnitureChange(kind, instance, candidate, revision); }

    internal DangoFurniturePreparation PrepareFurnitureChange(DangoFurnitureChangeKind kind,
        ulong instance, DangoFurnitureDefinition? candidate, ulong revision)
    { VerifyAccess(); RequireLegacyFurniture(); return world.PrepareFurnitureChange(kind, instance, candidate, revision); }

    internal DangoFurnitureCollectionFrame ApplyPreparedFurnitureChange(DangoPreparedFurnitureChange token)
    { VerifyAccess(); RequireLegacyFurniture(); return world.ApplyPreparedFurnitureChange(token); }

    internal void CancelPreparedFurnitureChange(DangoPreparedFurnitureChange token)
    { VerifyAccess(); RequireLegacyFurniture(); world.CancelPreparedFurnitureChange(token); }

    internal DangoFurnitureDefinition OrdinaryFurnitureDefinition(FurnitureItemInstance item)
    {
        VerifyAccess();
        RequireLegacyFurniture();
        item.Validate(FurnitureLayoutContract.OrdinaryRoom);
        var center = ToWorld(new LogicalPoint(item.Anchor.X, GroundY - item.Anchor.Y));
        return item.Kind switch
        {
            FurnitureItemKind.StaticPlatform => new(DangoFurnitureKind.StaticPlatform,
                Platform: new(center.X, center.Y - .35 / 2, 3.4, .35, .72)),
            FurnitureItemKind.RoundBall => new(DangoFurnitureKind.RoundBall,
                Ball: new(center.X * (4d / 3), center.Y, RadiusQ: 1.1, Mass: .02)),
            _ => throw new InvalidDataException("家具类型不受支持。"),
        };
    }

    internal FurnitureAnchor OrdinaryBallAnchor(DangoRoundBallState ball)
    {
        VerifyAccess();
        RequireLegacyFurniture();
        var center = ToHost(new(ball.CenterQX * .75, ball.CenterQY));
        return new(center.X, GroundY - center.Y);
    }

    internal DangoStaticPlatformDefinition OrdinaryFurnitureDefinition(FurniturePlatformInstance item)
    {
        VerifyAccess();
        RequireLegacyFurniture();
        item.Validate(FurnitureLayoutContract.OrdinaryRoom);
        var top = ToWorld(new LogicalPoint(item.Anchor.X, GroundY - item.Anchor.Y));
        return new(top.X, top.Y - .35 / 2, 3.4, .35, .72);
    }
}
