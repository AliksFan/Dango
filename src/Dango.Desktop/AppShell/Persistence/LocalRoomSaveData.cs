using System.IO;

namespace Dango.Desktop.AppShell.Persistence;

public sealed record LocalRoomCameraSaveData(
    string Mode,
    string Preset,
    double RequestedZoomPercent,
    double ManualX,
    double ManualY);

public sealed record LocalRoomSaveData(
    Guid RoomId,
    string RoomType,
    string TemplateId,
    int TemplateVersion,
    double? ResumeCenterX,
    LocalRoomCameraSaveData Camera);

/// <summary>Two independent local rooms; server room membership is never inferred from these IDs.</summary>
public sealed record LocalRoomSaveState(
    int SchemaVersion,
    LocalRoomSaveData OrdinaryRoom,
    LocalRoomSaveData DesktopPetRoom)
{
    public const int CurrentSchemaVersion = 1;

    public static LocalRoomSaveState CreateDefault() => new(CurrentSchemaVersion,
        new(Guid.NewGuid(), "ordinary", "ordinary-default", 1, null, new("follow", "standard", 100, 0, 0)),
        new(Guid.NewGuid(), "desktopPet", "desktop-pet-default", 1, null, new("follow", "standard", 100, 0, 0)));

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || OrdinaryRoom is null || DesktopPetRoom is null)
            throw new InvalidDataException("本地房间存档版本或房间字段无法识别。");
        ValidateRoom(OrdinaryRoom, "ordinary", "ordinary-default");
        ValidateRoom(DesktopPetRoom, "desktopPet", "desktop-pet-default");
        if (OrdinaryRoom.RoomId == DesktopPetRoom.RoomId)
            throw new InvalidDataException("两个本地房间不能使用相同标识。");
    }

    private static void ValidateRoom(LocalRoomSaveData room, string type, string template)
    {
        if (room.RoomId == Guid.Empty || room.RoomType != type || room.TemplateId != template || room.TemplateVersion != 1)
            throw new InvalidDataException("本地房间标识、类型或模板无法识别。");
        if (room.ResumeCenterX is double x && !ValidCoordinate(x))
            throw new InvalidDataException("本地房间逻辑位置超出允许范围。");
        var camera = room.Camera;
        if (camera is null || camera.Mode is not ("follow" or "edgeFollow" or "manual") ||
            camera.Preset is not ("standard" or "near" or "whole" or "custom") ||
            !double.IsFinite(camera.RequestedZoomPercent) || camera.RequestedZoomPercent is <= 0 or > 130 ||
            !ValidCoordinate(camera.ManualX) || !ValidCoordinate(camera.ManualY))
            throw new InvalidDataException("本地房间相机字段无法识别或超出允许范围。");
    }

    private static bool ValidCoordinate(double value) => double.IsFinite(value) && Math.Abs(value) <= 1_000_000;
}
