using Dango.Physics.Admission;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class AdmissionCoordinateTests
{
    [TestMethod]
    public void UsesFloorOriginAndDistinctHorizontalVerticalUnits()
    {
        Assert.AreEqual(new RoomCenter(1080, 0), OrdinaryRoomCoordinateMap.ToRoom(0, -.72));
        Assert.AreEqual(new RoomCenter(1160, 60), OrdinaryRoomCoordinateMap.ToRoom(1, .28));
        Assert.AreEqual(24, OrdinaryRoomCoordinateMap.ToRoom(-13.2, 0).X, 1e-10);
        Assert.AreEqual(2136, OrdinaryRoomCoordinateMap.ToRoom(13.2, 0).X, 1e-10);
    }

    [TestMethod]
    [DataRow(-13.2, -.72)]
    [DataRow(0, 0)]
    [DataRow(1.4, 1.65)]
    [DataRow(13.2, 4.5)]
    public void MappingIsInvertibleWithoutWindowOrDpi(double x, double y)
    {
        var room = OrdinaryRoomCoordinateMap.ToRoom(x, y);
        var native = OrdinaryRoomCoordinateMap.ToNative(room.X, room.Height);
        Assert.AreEqual(x, native.X, 1e-12);
        Assert.AreEqual(y, native.Y, 1e-12);
    }

    [TestMethod]
    public void InvalidInputAndOverflowDoNotBecomeRoomPositions()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OrdinaryRoomCoordinateMap.ToRoom(invalid, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OrdinaryRoomCoordinateMap.ToRoom(0, invalid));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OrdinaryRoomCoordinateMap.ToNative(invalid, 0));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OrdinaryRoomCoordinateMap.ToNative(0, invalid));
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OrdinaryRoomCoordinateMap.ToRoom(double.MaxValue, 0));
    }
}
