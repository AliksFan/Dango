namespace Dango.Desktop.AppShell.Rooms;

internal enum PorchTargetKind { Preview, LocalRoom, DesktopPetRoom, Room, Create, Archive, Visit, Online }
internal sealed record PorchTargetRequest(PorchTargetKind Kind, Guid? RoomId = null);
