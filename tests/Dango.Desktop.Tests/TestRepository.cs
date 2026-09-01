using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests;

internal static class TestRepository
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
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
