using System.Text.Json;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests;

[TestClass]
public sealed class WorkspaceAcceptanceTests
{
    [TestMethod]
    public void CoreProjectTargetsNet10WithoutDesktopDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "src", "Dango.Core", "Dango.Core.csproj");

        Assert.IsTrue(File.Exists(projectPath), $"缺少 Core 项目：{projectPath}");

        var project = XDocument.Load(projectPath);
        Assert.AreEqual("net10.0", Property(project, "TargetFramework"));
        Assert.IsNull(Property(project, "UseWPF"), "Core 不得启用 WPF。");
        Assert.IsFalse(
            project.Descendants("ProjectReference")
                .Any(reference => ((string?)reference.Attribute("Include"))?.Contains("Desktop", StringComparison.OrdinalIgnoreCase) == true),
            "Core 不得反向引用 Desktop。");
    }

    [TestMethod]
    public void PlaceholderManifestExistsAndListsOnlyRepositoryFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(repositoryRoot, "assets", "placeholder", "manifest.json");

        Assert.IsTrue(File.Exists(manifestPath), $"缺少占位资产清单：{manifestPath}");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.IsNotEmpty(files, "占位资产清单不得为空。");

        foreach (var file in files)
        {
            var relativePath = file.GetProperty("path").GetString();
            Assert.IsNotNull(relativePath);
            Assert.IsFalse(Path.IsPathRooted(relativePath), "清单不得包含绝对路径。");
            Assert.IsTrue(
                File.Exists(Path.Combine(repositoryRoot, "assets", "placeholder", relativePath)),
                $"清单文件不存在：{relativePath}");
        }
    }

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
