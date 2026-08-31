using ClosedXML.Excel;
using FileManage.Core.Reporting;
using FileManage.Infrastructure.Reporting;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// Excel 报表写入器：真实 .xlsx 落盘并回读校验表头与数据。
/// </summary>
public class ExcelReportWriterTests : IDisposable
{
    private readonly TempDir _target = new();
    private readonly ExcelReportWriter _writer = new();

    public void Dispose() => _target.Dispose();

    [Fact]
    public void Write_CreatesXlsxWithHeadersAndRows()
    {
        var rows = new List<ClassificationReportRow>
        {
            new()
            {
                OriginalName = "a.pdf",
                OriginalPath = @"D:\src\a.pdf",
                NewName = "合同_a.pdf",
                NewPath = @"D:\dst\PDF\合同_a.pdf",
                Category = "PDF",
                Operation = "重命名+复制",
                Conflict = "无冲突",
                RuleName = "PDF"
            },
            new()
            {
                OriginalName = "b.jpg",
                OriginalPath = @"D:\src\b.jpg",
                NewName = "b.jpg",
                NewPath = @"D:\dst\图片\b.jpg",
                Category = "图片",
                Operation = "移动",
                Conflict = "文件已存在",
                RuleName = "图片"
            }
        };

        var path = _writer.Write(_target.Path, "报表.xlsx", rows);

        Assert.Equal(Path.Combine(_target.Path, "报表.xlsx"), path);
        Assert.True(File.Exists(path));

        using var workbook = new XLWorkbook(path);
        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("文件分类整理报表", sheet.Name);

        Assert.Equal("原文件名", sheet.Cell(1, 1).GetString());
        Assert.Equal("原文件完整路径", sheet.Cell(1, 2).GetString());
        Assert.Equal("新文件名", sheet.Cell(1, 3).GetString());
        Assert.Equal("新文件完整路径", sheet.Cell(1, 4).GetString());
        Assert.Equal("分类", sheet.Cell(1, 5).GetString());
        Assert.Equal("操作", sheet.Cell(1, 6).GetString());
        Assert.Equal("冲突", sheet.Cell(1, 7).GetString());
        Assert.Equal("文件类型", sheet.Cell(1, 8).GetString());

        Assert.Equal(3, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("a.pdf", sheet.Cell(2, 1).GetString());
        Assert.Equal(@"D:\src\a.pdf", sheet.Cell(2, 2).GetString());
        Assert.Equal("重命名+复制", sheet.Cell(2, 6).GetString());
        Assert.Equal("无冲突", sheet.Cell(2, 7).GetString());
        Assert.Equal("PDF", sheet.Cell(2, 8).GetString());
        Assert.Equal("文件已存在", sheet.Cell(3, 7).GetString());
    }

    [Fact]
    public void Write_CreatesTargetDirectoryWhenMissing()
    {
        var nested = Path.Combine(_target.Path, "nested", "deep");

        var path = _writer.Write(nested, "报表.xlsx", []);

        Assert.True(File.Exists(path));
    }
}
