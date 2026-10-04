namespace Dango.Desktop.AppShell.Rooms;

/// <summary>
/// A horizontal camera in logical world units. Consumers apply the same scale to
/// the resulting pet, door and ground coordinates; no physical or network state is moved.
/// </summary>
public sealed class PorchCameraController
{
    private const double MaximumElapsedMilliseconds = 100;
    private readonly double worldWidth;
    private readonly double followMilliseconds;

    public PorchCameraController(double worldWidth = 2160, double followMs = 140)
    {
        RequirePositiveFinite(worldWidth, nameof(worldWidth));
        RequirePositiveFinite(followMs, nameof(followMs));
        this.worldWidth = worldWidth;
        followMilliseconds = followMs;
    }

    public double CameraX { get; private set; }

    public void Reset(double petX, double viewportWidth) => CameraX = Target(petX, viewportWidth);

    public void Resize(double petX, double viewportWidth) => Reset(petX, viewportWidth);

    public double Update(double petX, double viewportWidth, double elapsedMilliseconds, bool reduceMotion = false)
    {
        RequireFinite(elapsedMilliseconds, nameof(elapsedMilliseconds));
        var target = Target(petX, viewportWidth);
        var maximum = Math.Max(0, worldWidth - viewportWidth);
        var previous = Math.Clamp(CameraX, 0, maximum);
        var elapsed = Math.Clamp(elapsedMilliseconds, 0, MaximumElapsedMilliseconds);
        var alpha = reduceMotion ? 1 : 1 - Math.Exp(-elapsed / followMilliseconds);
        CameraX = Math.Clamp(previous + (target - previous) * alpha, 0, maximum);
        return CameraX;
    }

    public double WorldToScreen(double worldX) => WorldToScreen(worldX, 1);

    public double WorldToScreen(double worldX, double scale)
    {
        RequireFinite(worldX, nameof(worldX));
        RequirePositiveFinite(scale, nameof(scale));
        return (worldX - CameraX) * scale;
    }

    public static double ClampPetX(double petX, double worldWidth = 2160, double edgeInset = 0)
    {
        RequireFinite(petX, nameof(petX));
        RequirePositiveFinite(worldWidth, nameof(worldWidth));
        RequireFinite(edgeInset, nameof(edgeInset));
        if (edgeInset < 0 || edgeInset > worldWidth / 2)
            throw new ArgumentOutOfRangeException(nameof(edgeInset));
        return Math.Clamp(petX, edgeInset, worldWidth - edgeInset);
    }

    private double Target(double petX, double viewportWidth)
    {
        RequireFinite(petX, nameof(petX));
        RequirePositiveFinite(viewportWidth, nameof(viewportWidth));
        return Math.Clamp(petX - viewportWidth / 2, 0, Math.Max(0, worldWidth - viewportWidth));
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }

    private static void RequirePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
