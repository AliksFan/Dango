using System.IO;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.PhysicsLab;

/// <summary>Owned fictional smoke data; never reads or writes the daily appearance.</summary>
internal static class LabAppearanceFixture
{
    internal static string Create(string session)
    {
        var directory = Path.Combine(session, "fictional-appearance");
        Directory.CreateDirectory(directory);
        new AppearanceSettingsStore().Save(Path.Combine(directory, "appearance.json"), AppearanceSettings.CreateDefault());
        new AccessorySettingsStore().Save(directory, new([
            new("fictional-back", "Fictional rear", "builtin-top-hat", 64, 64)
            { Layer = AccessoryLayer.Behind, BodyAnchorX = .3, BodyAnchorY = .08, ImagePivotY = .9375, SizePercent = 65,
                Softness = 40, ConnectionStrength = 65, LockRotation = false, GravityInfluence = 20 },
            new("fictional-front", "Fictional front", "builtin-sprout", 64, 64)
            { Layer = AccessoryLayer.Front, BodyAnchorX = .72, BodyAnchorY = .06, ImagePivotY = .9375, SizePercent = 65,
                Softness = 70, ConnectionStrength = 70, LockRotation = false, GravityInfluence = 30 },
        ]));
        return Path.Combine(directory, "settings.json");
    }
}
