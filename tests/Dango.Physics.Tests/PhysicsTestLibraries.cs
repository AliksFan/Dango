namespace Dango.Physics.Tests;

internal static class PhysicsTestLibraries
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dango.slnx"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }
    internal static string Unified => Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
        Path.Combine(Root, "artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll");
    internal static string AcceptedThree => Path.Combine(Root,
        "artifacts/native-spike/physics-three-preview-build/Release/dango_physics_three_preview.dll");
}
