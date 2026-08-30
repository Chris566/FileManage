using FileManage.Core.Duplicate;
using FileManage.Core.Models;
using FileManage.Infrastructure.FileSystem;

namespace FileManage.Core.Tests;

/// <summary>
/// 重复检测集成测试：临时目录真实文件 + SHA-256。
/// </summary>
public class DuplicateDetectorTests
{
    private static (DuplicateDetector Detector, TempDir Dir) Create()
    {
        return (new DuplicateDetector(new FileSystemService()), new TempDir());
    }

    private static FileItem Item(string fullPath, long size) => new()
    {
        FullPath = fullPath,
        Name = Path.GetFileName(fullPath),
        Extension = Path.GetExtension(fullPath),
        SizeBytes = size,
        ModifiedTime = TestHelper.DefaultModifiedTime
    };

    [Fact]
    public void Detect_SameContentDifferentNames_GroupsTogether()
    {
        var (detector, dir) = Create();
        using var _ = dir;

        dir.CreateFileWithContent("a.txt", "重复内容");
        dir.CreateFileWithContent("b.txt", "重复内容");
        dir.CreateFileWithContent("c.txt", "独立内容");

        var files = new[]
        {
            Item(Path.Combine(dir.Path, "a.txt"), 12),
            Item(Path.Combine(dir.Path, "b.txt"), 12),
            Item(Path.Combine(dir.Path, "c.txt"), 13)
        };

        var result = detector.Detect(files);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, f => f.Name == "a.txt");
        Assert.Contains(group.Files, f => f.Name == "b.txt");
        Assert.Equal(2, result.DuplicateFileCount); // a、b 两个文件内容重复
        Assert.Equal(12, result.WastedBytes);
    }

    [Fact]
    public void Detect_SameSizeDifferentContent_NotGrouped()
    {
        var (detector, dir) = Create();
        using var _ = dir;

        dir.CreateFileWithContent("x.bin", "AAAA");
        dir.CreateFileWithContent("y.bin", "BBBB");

        var files = new[]
        {
            Item(Path.Combine(dir.Path, "x.bin"), 4),
            Item(Path.Combine(dir.Path, "y.bin"), 4)
        };

        var result = detector.Detect(files);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public void Detect_EmptyFiles_NotGrouped()
    {
        var (detector, dir) = Create();
        using var _ = dir;

        dir.CreateFile("e1.bin", 0);
        dir.CreateFile("e2.bin", 0);

        var files = new[]
        {
            Item(Path.Combine(dir.Path, "e1.bin"), 0),
            Item(Path.Combine(dir.Path, "e2.bin"), 0)
        };

        Assert.Empty(detector.Detect(files).Groups);
    }

    [Fact]
    public void Detect_UniqueFile_NoGroups()
    {
        var (detector, dir) = Create();
        using var _ = dir;

        dir.CreateFileWithContent("solo.txt", "唯一");

        var result = detector.Detect([Item(Path.Combine(dir.Path, "solo.txt"), 6)]);

        Assert.Empty(result.Groups);
        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.DuplicateFileCount);
    }
}
