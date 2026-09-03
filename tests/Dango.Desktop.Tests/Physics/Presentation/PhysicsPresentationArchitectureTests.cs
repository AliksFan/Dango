using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class PhysicsPresentationArchitectureTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "Box2D",
        "NativeLibrary",
        "dango_box2d",
        "IPhysicsPresentationWorld",
        "Box2DPhysicsPresentationAdapter",
    ];

    [TestMethod]
    public void CoreAndDesktopHostDoNotDependOnPhysicsPresentationOrNativeBox2D()
    {
        AssertDirectoryHasNoForbiddenTokens(Path.Combine(TestRepository.Root, "src", "Dango.Core"));
        AssertDirectoryHasNoForbiddenTokens(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "DesktopHost"));
    }

    private static void AssertDirectoryHasNoForbiddenTokens(string directory)
    {
        var violations = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, index }))
            .Where(item => ForbiddenTokens.Any(token =>
                item.line.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(item => $"{Path.GetRelativePath(TestRepository.Root, item.path)}:{item.index + 1}")
            .ToArray();

        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }
}
