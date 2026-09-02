using System.Text;

namespace Dango.Desktop.Verification;

public static class VerificationMarkdownExporter
{
    public static string Export(VerificationDefinition definition, VerificationSession session)
    {
        VerificationSessionValidator.Validate(definition, session);

        var unverified = session.Results.Count(result => result.Outcome == ManualVerificationOutcome.Unverified);
        var failed = session.Results.Count(result => result.Outcome == ManualVerificationOutcome.Failed);
        var unable = session.Results.Count(result => result.Outcome == ManualVerificationOutcome.UnableToRun);
        var builder = new StringBuilder();
        var taskId = definition.Title.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        builder.AppendLine($"# {definition.Title}记录");
        builder.AppendLine();
        builder.AppendLine($"> 会话：`{EscapeInline(session.SessionId)}`");
        builder.AppendLine($"> 开始时间：{session.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"> 结论：人工失败 {failed} 项，未验证 {unverified} 项，无法执行 {unable} 项；本文不代表 {taskId} 自动通过。");
        builder.AppendLine();
        builder.AppendLine("## 环境摘要");
        builder.AppendLine();
        builder.AppendLine($"- 应用版本：{EscapeInline(session.Environment.ApplicationVersion)}");
        builder.AppendLine($"- 构建标识：{EscapeInline(session.Environment.BuildIdentity)}");
        builder.AppendLine($"- 宿主模式：{EscapeInline(session.Environment.HostMode)}");
        builder.AppendLine($"- OS Build：{EscapeInline(session.Environment.OsBuild)}");
        builder.AppendLine($"- 显示摘要：{EscapeInline(session.Environment.DisplaySummary)}");
        builder.AppendLine();
        AppendEvidenceSections(builder);
        builder.AppendLine("## 人工验收");
        builder.AppendLine();
        builder.AppendLine("| 类别 | 项目 | 结果 | 备注 |");
        builder.AppendLine("|---|---|---|---|");

        var definitions = definition.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var result in session.Results)
        {
            var item = definitions[result.ItemId];
            builder.Append("| ")
                .Append(EscapeTable(item.Category))
                .Append(" | ")
                .Append(EscapeTable(item.Title))
                .Append(" | ")
                .Append(OutcomeText(result.Outcome))
                .Append(" | ")
                .Append(EscapeTable(result.Notes))
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## 交付状态");
        builder.AppendLine();
        builder.AppendLine("- 提交、合并、推送与部署状态：未由客户端采集，请在交付报告中单独记录。");
        return builder.ToString();
    }

    private static void AppendEvidenceSections(StringBuilder builder)
    {
        builder.AppendLine("## 静态门禁");
        builder.AppendLine();
        builder.AppendLine("- 未导入。客户端不会自行声明格式、构建或测试通过。");
        builder.AppendLine();
        builder.AppendLine("## 集成门禁");
        builder.AppendLine();
        builder.AppendLine("- Phase 1 当前不涉及网络或数据库，本项不适用，不记为通过。");
        builder.AppendLine();
        builder.AppendLine("## 实时行为");
        builder.AppendLine();
        builder.AppendLine("- 宿主可见性、穿透、焦点、全屏和 DPI 结论以以下人工记录为准；未填写项保持未验证。");
        builder.AppendLine();
    }

    private static string OutcomeText(ManualVerificationOutcome outcome) => outcome switch
    {
        ManualVerificationOutcome.Unverified => "未验证",
        ManualVerificationOutcome.Passed => "人工通过",
        ManualVerificationOutcome.Failed => "人工失败",
        ManualVerificationOutcome.UnableToRun => "无法执行",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static string EscapeInline(string value) =>
        value.Replace("`", "'", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private static string EscapeTable(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings("<br>");
}
