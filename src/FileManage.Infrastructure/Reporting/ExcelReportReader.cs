using ClosedXML.Excel;
using FileManage.Core.Abstractions;
using FileManage.Core.Reporting;

namespace FileManage.Infrastructure.Reporting;

/// <summary>
/// 分类整理报表 xlsx 读取器（ClosedXML）。
/// 表结构需与 ExcelReportWriter 输出一致：8 列固定表头。
/// </summary>
public sealed class ExcelReportReader : IClassificationReportReader
{
    public IReadOnlyList<RestoreEntry> Read(string reportPath)
    {
        using var workbook = new XLWorkbook(reportPath);
        var sheet = workbook.Worksheets.First();

        var entries = new List<RestoreEntry>();

        // 第 1 行为表头，数据从第 2 行开始
        var lastRow = sheet.LastRowUsed();
        if (lastRow is null)
        {
            return entries;
        }

        for (var row = 2; row <= lastRow.RowNumber(); row++)
        {
            var originalName = sheet.Cell(row, 1).GetString().Trim();
            var originalPath = sheet.Cell(row, 2).GetString().Trim();
            var newName = sheet.Cell(row, 3).GetString().Trim();
            var newPath = sheet.Cell(row, 4).GetString().Trim();
            var category = sheet.Cell(row, 5).GetString().Trim();
            var ruleName = sheet.Cell(row, 8).GetString().Trim();

            if (string.IsNullOrEmpty(originalPath) || string.IsNullOrEmpty(newPath))
            {
                continue;
            }

            entries.Add(new RestoreEntry
            {
                OriginalName = originalName,
                OriginalPath = originalPath,
                NewName = newName,
                NewPath = newPath,
                Category = category,
                RuleName = ruleName
            });
        }

        return entries;
    }
}
