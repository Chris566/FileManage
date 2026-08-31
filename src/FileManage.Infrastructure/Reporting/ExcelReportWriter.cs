using ClosedXML.Excel;
using FileManage.Core.Abstractions;
using FileManage.Core.Reporting;

namespace FileManage.Infrastructure.Reporting;

/// <summary>
/// 分类整理报表 xlsx 写入器（ClosedXML，Excel 2016 及以上版本兼容）。
/// 表结构：8 列固定表头 + 首行冻结 + 列宽预设。
/// </summary>
public sealed class ExcelReportWriter : IClassificationReportWriter
{
    private static readonly string[] Headers =
    [
        "原文件名", "原文件完整路径", "新文件名", "新文件完整路径",
        "分类", "操作", "冲突", "文件类型"
    ];

    private static readonly double[] ColumnWidths = [34, 72, 34, 72, 22, 16, 30, 22];

    public string Write(string targetDirectory, string fileName, IReadOnlyList<ClassificationReportRow> rows)
    {
        Directory.CreateDirectory(targetDirectory);
        var fullPath = Path.Combine(targetDirectory, fileName);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("文件分类整理报表");

        for (var c = 0; c < Headers.Length; c++)
        {
            var header = sheet.Cell(1, c + 1);
            header.Value = Headers[c];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(c + 1).Width = ColumnWidths[c];
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            sheet.Cell(r + 2, 1).Value = row.OriginalName;
            sheet.Cell(r + 2, 2).Value = row.OriginalPath;
            sheet.Cell(r + 2, 3).Value = row.NewName;
            sheet.Cell(r + 2, 4).Value = row.NewPath;
            sheet.Cell(r + 2, 5).Value = row.Category;
            sheet.Cell(r + 2, 6).Value = row.Operation;
            sheet.Cell(r + 2, 7).Value = row.Conflict;
            sheet.Cell(r + 2, 8).Value = row.RuleName;
        }

        sheet.SheetView.FreezeRows(1);
        workbook.SaveAs(fullPath);
        return fullPath;
    }
}
