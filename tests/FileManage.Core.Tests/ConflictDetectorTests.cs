using FileManage.Core.Planning;
using FileManage.Infrastructure.FileSystem;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// ConflictDetector 集成测试：临时目录真实 IO（设计文档 §4.4 四类冲突）。
/// </summary>
public class ConflictDetectorTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ConflictDetector _detector = new(new FileSystemService());

    private RenameCandidate Candidate(string itemName, string requestedName, string? itemDirectory = null)
    {
        var item = TestHelper.Item(itemName, itemDirectory ?? @"D:\other");
        return new RenameCandidate(item, requestedName);
    }

    [Fact]
    public void Detect_NoConflict_ReturnsNone()
    {
        _dir.CreateFile("已有.pdf");

        var report = _detector.Detect([Candidate("报告.pdf", "新报告.pdf")], _dir.Path);

        var item = Assert.Single(report.Items);
        Assert.Equal(ConflictType.None, item.Type);
        Assert.Equal("新报告.pdf", item.FinalName);
        Assert.False(report.HasBlockingConflicts);
    }

    [Fact]
    public void Detect_PlanDuplicate_AutoAppendsCounter()
    {
        var report = _detector.Detect(
        [
            Candidate("a1.pdf", "same.pdf"),
            Candidate("a2.pdf", "same.pdf"),
            Candidate("a3.pdf", "same.pdf")
        ], _dir.Path);

        Assert.Equal(3, report.Items.Count);
        Assert.Equal(ConflictType.None, report.Items[0].Type);
        Assert.Equal("same.pdf", report.Items[0].FinalName);
        Assert.Equal(ConflictType.PlanDuplicate, report.Items[1].Type);
        Assert.Equal("same_2.pdf", report.Items[1].FinalName);
        Assert.Equal(ConflictType.PlanDuplicate, report.Items[2].Type);
        Assert.Equal("same_3.pdf", report.Items[2].FinalName);
    }

    [Fact]
    public void Detect_PlanDuplicate_SkipsNameExistingOnDisk()
    {
        _dir.CreateFile("same_2.pdf");

        var report = _detector.Detect(
        [
            Candidate("a1.pdf", "same.pdf"),
            Candidate("a2.pdf", "same.pdf")
        ], _dir.Path);

        Assert.Equal("same.pdf", report.Items[0].FinalName);
        // _2 已被磁盘占用 → 跳到 _3
        Assert.Equal("same_3.pdf", report.Items[1].FinalName);
    }

    [Fact]
    public void Detect_PlanDuplicate_FileWithoutExtension()
    {
        var report = _detector.Detect(
        [
            Candidate("a1", "same"),
            Candidate("a2", "same")
        ], _dir.Path);

        Assert.Equal("same_2", report.Items[1].FinalName);
    }

    [Fact]
    public void Detect_TargetExists_MarkedNotRenamed()
    {
        _dir.CreateFile("占用.pdf");

        var report = _detector.Detect([Candidate("报告.pdf", "占用.pdf")], _dir.Path);

        var item = Assert.Single(report.Items);
        Assert.Equal(ConflictType.TargetExists, item.Type);
        Assert.Equal("占用.pdf", item.FinalName); // 不自动改号，执行期按覆盖策略处理
        Assert.False(report.HasBlockingConflicts);
    }

    [Fact]
    public void Detect_PathTooLong_Blocked()
    {
        var longName = new string('长', 220) + ".pdf";

        var report = _detector.Detect([Candidate("a.pdf", longName)], _dir.Path);

        var item = Assert.Single(report.Items);
        Assert.Equal(ConflictType.PathTooLong, item.Type);
        Assert.True(report.HasBlockingConflicts);
    }

    [Theory]
    [InlineData("a<b.pdf")]
    [InlineData("a|b.pdf")]
    [InlineData("a?b.pdf")]
    [InlineData("a*b.pdf")]
    [InlineData("a:b.pdf")]
    public void Detect_InvalidChars_Blocked(string requestedName)
    {
        var report = _detector.Detect([Candidate("a.pdf", requestedName)], _dir.Path);

        var item = Assert.Single(report.Items);
        Assert.Equal(ConflictType.InvalidChars, item.Type);
        Assert.True(report.HasBlockingConflicts);
    }

    [Fact]
    public void Detect_RenameToSelf_NoConflict()
    {
        // 大小写变化：源文件自身在磁盘上"存在"，不应误报 TargetExists
        var sourcePath = _dir.CreateFile("报告.pdf");

        var item = TestHelper.Item("报告.pdf", directory: _dir.Path);
        var report = _detector.Detect([new RenameCandidate(item, "报告.PDF")], _dir.Path);

        var result = Assert.Single(report.Items);
        Assert.Equal(ConflictType.None, result.Type);
        Assert.Equal(sourcePath, Path.Combine(_dir.Path, result.FinalName), ignoreCase: true);
    }

    public void Dispose() => _dir.Dispose();
}
