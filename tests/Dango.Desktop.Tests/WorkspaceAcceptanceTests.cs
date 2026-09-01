using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests;

[TestClass]
public sealed class WorkspaceAcceptanceTests
{
    [TestMethod]
    public void DesktopProjectTargetsWindowsNet10AndUsesWpf()
    {
        var projectPath = DesktopProjectPath();
        Assert.IsTrue(File.Exists(projectPath), $"缺少 Desktop 项目：{projectPath}");

        var project = XDocument.Load(projectPath);
        Assert.AreEqual("net10.0-windows", Property(project, "TargetFramework"));
        Assert.AreEqual("true", Property(project, "UseWPF"), ignoreCase: true);
    }

    [TestMethod]
    public void DesktopDependsOnCoreInOneDirection()
    {
        var projectPath = DesktopProjectPath();
        Assert.IsTrue(File.Exists(projectPath), $"缺少 Desktop 项目：{projectPath}");

        var project = XDocument.Load(projectPath);
        var references = project.Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(reference => reference is not null)
            .ToArray();

        CollectionAssert.Contains(references, @"..\Dango.Core\Dango.Core.csproj");
    }

    private static string DesktopProjectPath() =>
        Path.Combine(FindRepositoryRoot(), "src", "Dango.Desktop", "Dango.Desktop.csproj");

    private static string? Property(XDocument project, string name) =>
        project.Descendants(name).Select(element => element.Value).FirstOrDefault();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("无法从测试输出目录定位仓库根目录。");
        return string.Empty;
    }
}
