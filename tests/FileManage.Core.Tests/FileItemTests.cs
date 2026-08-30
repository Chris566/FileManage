using FileManage.Core.Models;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// M0 冒烟测试：验证 Core 程序集引用链路与 FileItem 模型基础行为。
/// M1 起替换为 NameEngine/RuleEngine/ConflictDetector 等真实用例。
/// </summary>
public class FileItemTests
{
    [Fact]
    public void Constructor_PropertiesRoundTrip()
    {
        var modified = new DateTime(2026, 8, 30, 12, 0, 0);

        var item = new FileItem
        {
            FullPath = @"D:\data\报告.pdf",
            Name = "报告.pdf",
            Extension = ".pdf",
            SizeBytes = 1024,
            ModifiedTime = modified
        };

        Assert.Equal(@"D:\data\报告.pdf", item.FullPath);
        Assert.Equal("报告.pdf", item.Name);
        Assert.Equal(".pdf", item.Extension);
        Assert.Equal(1024, item.SizeBytes);
        Assert.Equal(modified, item.ModifiedTime);
        Assert.Null(item.MatchedCategory);
        Assert.Null(item.ExifDate);
    }
}
