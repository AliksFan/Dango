using System.IO;
using System.Text.Json;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>The complete browser-editable wearing subset. Identity, images and equipment state stay native.</summary>
internal static class AccessoryWearParameters
{
    internal static object ToPayload(AccessoryItem item) => new
    {
        bodyAnchorX = item.BodyAnchorX, bodyAnchorY = item.BodyAnchorY,
        sizePercent = item.SizePercent, baseAngleDegrees = item.BaseAngleDegrees,
        connectionStrength = item.ConnectionStrength, maxConnectionTravelPercent = item.MaxConnectionTravelPercent,
        rotationDamping = item.RotationDamping, lockRotation = item.LockRotation,
        returnStrength = item.ReturnStrength, maxSwingDegrees = item.MaxSwingDegrees,
        limitSwing = item.LimitSwing, gravityInfluence = item.GravityInfluence, layer = item.Layer.ToString(),
    };

    internal static AccessoryItem Read(JsonElement payload, AccessoryItem current)
    {
        var f = AccessoryEditorBridge.ExactObject(payload, ["bodyAnchorX", "bodyAnchorY", "sizePercent", "baseAngleDegrees",
            "connectionStrength", "maxConnectionTravelPercent", "rotationDamping", "lockRotation", "returnStrength",
            "maxSwingDegrees", "limitSwing", "gravityInfluence", "layer"]);
        var layer = f["layer"].GetString() switch
        {
            "Behind" => AccessoryLayer.Behind, "Front" => AccessoryLayer.Front,
            _ => throw new InvalidDataException("Invalid wearing layer."),
        };
        return current with
        {
            BodyAnchorX = Number(f["bodyAnchorX"], 0, 1), BodyAnchorY = Number(f["bodyAnchorY"], 0, 1),
            SizePercent = Number(f["sizePercent"], 10, 400), BaseAngleDegrees = Number(f["baseAngleDegrees"], -180, 180),
            ConnectionStrength = Number(f["connectionStrength"], 0, 100), MaxConnectionTravelPercent = Number(f["maxConnectionTravelPercent"], 0, 200),
            RotationDamping = Number(f["rotationDamping"], 0, 100), LockRotation = f["lockRotation"].GetBoolean(),
            ReturnStrength = Number(f["returnStrength"], 0, 100), MaxSwingDegrees = Number(f["maxSwingDegrees"], 0, 180),
            LimitSwing = f["limitSwing"].GetBoolean(), GravityInfluence = Number(f["gravityInfluence"], 0, 100), Layer = layer,
        };
    }

    internal static double Number(JsonElement value, double minimum, double maximum)
    {
        var number = value.GetDouble();
        if (!double.IsFinite(number) || number < minimum || number > maximum)
            throw new InvalidDataException("Wearing coordinate or parameter out of bounds.");
        return number;
    }
}

internal sealed record AccessorySimulationViewport(double X, double Y, double Width, double Height, bool Visible)
{
    internal static AccessorySimulationViewport Read(JsonElement payload)
    {
        var f = AccessoryEditorBridge.ExactObject(payload, ["x", "y", "width", "height", "visible"]);
        return new(AccessoryWearParameters.Number(f["x"], 0, 8192), AccessoryWearParameters.Number(f["y"], 0, 8192),
            AccessoryWearParameters.Number(f["width"], 0, 8192), AccessoryWearParameters.Number(f["height"], 0, 8192), f["visible"].GetBoolean());
    }
}
