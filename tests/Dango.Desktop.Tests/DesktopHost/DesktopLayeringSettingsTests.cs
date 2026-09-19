using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class DesktopLayeringSettingsTests
{
    [TestMethod]
    public void DefaultsPlaceDangoAndOrdinaryFurnitureBelowIcons()
    {
        var settings = DesktopLayeringSettings.CreateDefault();

        Assert.AreEqual(DesktopObjectLayer.BelowDesktopIcons, settings.DangoLayer);
        Assert.AreEqual(
            DesktopObjectLayer.BelowDesktopIcons,
            settings.FurnitureLayers[DesktopLayeringSettings.PrototypeCushionId]);
    }

    [TestMethod]
    public void FurnitureLayersRemainPerObject()
    {
        var settings = new DesktopLayeringSettings(
            DesktopObjectLayer.AboveDesktopIcons,
            new Dictionary<string, DesktopObjectLayer>
            {
                [DesktopLayeringSettings.PrototypeCushionId] = DesktopObjectLayer.AboveDesktopIcons,
                ["another-furniture"] = DesktopObjectLayer.BelowDesktopIcons,
            });

        DesktopLayeringSettingsValidator.Validate(settings);
        Assert.AreEqual(2, settings.FurnitureLayers.Count);
    }
}
