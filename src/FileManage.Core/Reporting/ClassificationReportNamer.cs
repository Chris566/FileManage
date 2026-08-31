using System.Globalization;

namespace FileManage.Core.Reporting;

/// <summary>
/// 报表文件命名器：源文件夹名称 + 执行时间（yyyyMMddHHmmss）+ 数字序号。
/// 序号从 1 开始，同一执行时间已存在同名报表时递增，确保不覆盖历史报表。
/// </summary>
public static class ClassificationReportNamer
{
    /// <summary>
    /// 生成报表文件名（不含目录）。
    /// </summary>
    /// <param name="sourceDirectory">源目录（取其末级文件夹名作为前缀）。</param>
    /// <param name="executionTime">执行时间（本地时间，精确到秒参与命名）。</param>
    /// <param name="fileExists">文件存在性探测（目标目录下是否已有同名报表）。</param>
    public static string BuildFileName(
        string sourceDirectory,
        DateTime executionTime,
        Func<string, bool> fileExists)
    {
        var sourceName = Path.GetFileName(
            sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(sourceName))
        {
            sourceName = "Report";
        }

        var timestamp = executionTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        for (var sequence = 1; ; sequence++)
        {
            var name = $"{sourceName}{timestamp}{sequence}.xlsx";

            if (!fileExists(name))
            {
                return name;
            }
        }
    }
}
