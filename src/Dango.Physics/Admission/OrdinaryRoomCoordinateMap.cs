namespace Dango.Physics.Admission;

/// <summary>Frozen P0 coordinates: horizontal room units and height above the floor.</summary>
public static class OrdinaryRoomCoordinateMap
{
    public const double WorldWidth = 2160;
    public const double HorizontalUnitsPerNativeUnit = 80;
    public const double VerticalUnitsPerNativeUnit = 60;
    public const double NativeGroundHeight = -.72;
    public const double NativeLeftBoundary = -13.2;
    public const double NativeRightBoundary = 13.2;

    public static RoomCenter ToRoom(double nativeCoreX, double nativeCoreY)
    {
        RequireFinite(nativeCoreX, nativeCoreY);
        var point = new RoomCenter(WorldWidth / 2 + nativeCoreX * HorizontalUnitsPerNativeUnit,
            (nativeCoreY - NativeGroundHeight) * VerticalUnitsPerNativeUnit);
        RequireFinite(point.X, point.Height);
        return point;
    }

    public static NativeCenter ToNative(double roomX, double heightAboveGround)
    {
        RequireFinite(roomX, heightAboveGround);
        return new((roomX - WorldWidth / 2) / HorizontalUnitsPerNativeUnit,
            heightAboveGround / VerticalUnitsPerNativeUnit + NativeGroundHeight);
    }

    private static void RequireFinite(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
    }
}

public readonly record struct RoomCenter(double X, double Height);
public readonly record struct NativeCenter(double X, double Y);
