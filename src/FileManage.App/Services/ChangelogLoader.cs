using System.IO;
using System.Reflection;

namespace FileManage.App.Services;

/// <summary>
/// 从程序集内嵌的 CHANGELOG.md 加载更新日志（每次 CI 打包前由 git tag 重建，含本次新版本）。
/// 单一读取入口，供 ChangelogWindow 与 UpdateWindow 共用，避免两处流读取逻辑漂移。
/// </summary>
public static class ChangelogLoader
{
    private const string ResourceName = "CHANGELOG.md";
    private const int DefaultPreviewLines = 30;

    /// <summary>返回完整更新日志（不可用时返回占位字符串）。</summary>
    public static string LoadFull()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return "更新日志内容不可用。";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 返回给定版本号的发布说明小节（以"## v{version}"为分隔），用于更新对话框在无网时作兜底。
    /// 找不到匹配小节时返回完整日志的前 N 行。
    /// </summary>
    public static string LoadVersionSection(string version, int previewLines = DefaultPreviewLines)
    {
        var full = LoadFull();
        if (string.IsNullOrWhiteSpace(full))
        {
            return "";
        }

        string marker = $"## v{version}";
        int index = full.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            marker = $"## v{version.TrimStart('v')}";
            index = full.IndexOf(marker, StringComparison.Ordinal);
        }

        if (index >= 0)
        {
            int after = index + marker.Length;
            // 下一个 "## " 标记或文档末为结束
            int next = full.IndexOf("\n## ", after, StringComparison.Ordinal);
            int end = next < 0 ? full.Length : next;
            return full.Substring(after, end - after).Trim(' ', '\r', '\n', '\t', '(', ')', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');
        }

        // 兜底：预览前 N 行
        var lines = full.Split(new[] { '\n' }, StringSplitOptions.None);
        return string.Join("\n", lines.Take(previewLines));
    }
}
