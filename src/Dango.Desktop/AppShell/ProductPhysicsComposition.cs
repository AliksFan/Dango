using System.IO;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell;

/// <summary>One product implementation choice, with distinct editor and room owners.</summary>
internal sealed class ProductPhysicsComposition
{
    internal static ProductPhysicsComposition Default { get; } = FromModules();
    private readonly Func<IBodyPreviewRuntime> createPreview;
    private readonly Func<StackedDangoSceneRuntime> createRoom;

    internal ProductPhysicsComposition(Func<IBodyPreviewRuntime> createPreview,
        Func<StackedDangoSceneRuntime> createRoom)
    {
        this.createPreview = createPreview ?? throw new ArgumentNullException(nameof(createPreview));
        this.createRoom = createRoom ?? throw new ArgumentNullException(nameof(createRoom));
    }

    internal static ProductPhysicsComposition FromModules(string? roomModulePath = null) => new(
        () => new BeltBodyRuntime(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll")) { SleepEnabled = true },
        () => StackedDangoSceneRuntime.CreateBeltRoom(roomModulePath ?? Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll")));

    internal IBodyPreviewRuntime CreatePreview() => createPreview();
    internal StackedDangoSceneRuntime CreateRoom() => createRoom();
}
