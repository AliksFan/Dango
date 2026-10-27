using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Persistence of the bound identity; preview state remains owned by its session.</summary>
public interface IAppearanceSettingsPersistence
{
    AppearanceSettingsLoadResult LoadOrDefault(string path);
    void Save(string path, DangoAppearanceConfiguration configuration);
}
