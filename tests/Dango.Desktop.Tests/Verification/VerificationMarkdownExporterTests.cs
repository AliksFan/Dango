using Dango.Desktop.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Verification;

[TestClass]
public sealed class VerificationMarkdownExporterTests
{
    [TestMethod]
    public void IncompleteSessionCannotClaimP1002Passed()
    {
        var definition = VerificationDefinition.CreateP1002Overlay();
        var session = CreateSession(definition);

        var markdown = VerificationMarkdownExporter.Export(definition, session);

        StringAssert.Contains(markdown, "## 静态门禁");
        StringAssert.Contains(markdown, "## 集成门禁");
        StringAssert.Contains(markdown, "## 实时行为");
        StringAssert.Contains(markdown, "## 人工验收");
        StringAssert.Contains(markdown, "## 交付状态");
        StringAssert.Contains(markdown, "未验证");
        Assert.IsFalse(markdown.Contains("P1-002 已通过", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FailureAndUnableToRunRemainVisibleInReport()
    {
        var definition = VerificationDefinition.CreateP1002Overlay();
        var session = CreateSession(definition);
        session.Results[0].Outcome = ManualVerificationOutcome.Failed;
        session.Results[0].Notes = "透明区域仍阻挡桌面";
        session.Results[1].Outcome = ManualVerificationOutcome.UnableToRun;
        session.Results[1].Notes = "当前设备没有第二种缩放配置";

        var markdown = VerificationMarkdownExporter.Export(definition, session);

        StringAssert.Contains(markdown, "人工失败");
        StringAssert.Contains(markdown, "透明区域仍阻挡桌面");
        StringAssert.Contains(markdown, "无法执行");
        StringAssert.Contains(markdown, "当前设备没有第二种缩放配置");
        Assert.IsFalse(markdown.Contains("P1-002 已通过", StringComparison.Ordinal));
    }

    private static VerificationSession CreateSession(VerificationDefinition definition) =>
        VerificationSession.Create(
            definition,
            new VerificationEnvironmentSnapshot("1.0.0", "test", "Overlay", "26100", "1 display / 100%"),
            new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(8)));
}
