using System.IO;

namespace Dango.Desktop.DesktopHost;

public enum DesktopObjectLayer
{
    BelowDesktopIcons,
    AboveDesktopIcons,
}

public sealed record DesktopLayeringSettings(
    DesktopObjectLayer DangoLayer,
    IReadOnlyDictionary<string, DesktopObjectLayer> FurnitureLayers)
{
    public const string PrototypeCushionId = "prototype-cushion";

    public static DesktopLayeringSettings CreateDefault() =>
        new(
            DesktopObjectLayer.BelowDesktopIcons,
            new Dictionary<string, DesktopObjectLayer>(StringComparer.Ordinal)
            {
                [PrototypeCushionId] = DesktopObjectLayer.BelowDesktopIcons,
            });

    public DesktopObjectLayer GetFurnitureLayer(string furnitureId) =>
        FurnitureLayers.TryGetValue(furnitureId, out var layer)
            ? layer
            : DesktopObjectLayer.BelowDesktopIcons;
}

public static class DesktopLayeringSettingsValidator
{
    public static void Validate(DesktopLayeringSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.DangoLayer))
        {
            throw new InvalidDataException("团子桌面层级设置无效。");
        }

        if (settings.FurnitureLayers is null || settings.FurnitureLayers.Count > 128)
        {
            throw new InvalidDataException("家具桌面层级设置无效。");
        }

        foreach (var item in settings.FurnitureLayers)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 128 || !Enum.IsDefined(item.Value))
            {
                throw new InvalidDataException("家具桌面层级设置无效。");
            }
        }
    }
}

public interface IDesktopLayeringControl
{
    DesktopLayeringSettings GetLayeringSettings();

    void ApplyLayeringSettings(DesktopLayeringSettings settings);
}
