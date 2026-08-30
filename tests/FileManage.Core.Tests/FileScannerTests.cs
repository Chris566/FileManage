using FileManage.Core.Scanning;
using FileManage.Infrastructure.FileSystem;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// FileScanner 集成测试：临时目录真实 IO（设计文档 §4.1）。
/// </summary>
public class FileScannerTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileScanner _scanner = new(new FileSystemService());

    [Fact]
    public void Scan_Depth0_OnlyTopLevel()
    {
        _dir.CreateFile("顶层.pdf");
        _dir.SubDir("子目录");
        File.WriteAllBytes(System.IO.Path.Combine(_dir.Path, "子目录", "深层.pdf"), [1, 2, 3]);

        var result = _scanner.Scan(new ScanOptions { RootDirectory = _dir.Path });

        Assert.Single(result.Items);
        Assert.Equal("顶层.pdf", result.Items[0].Name);
    }

    [Fact]
    public void Scan_Recursive_IncludesSubdirectories()
    {
        _dir.CreateFile("顶层.pdf");
        var sub = _dir.SubDir("子目录");
        File.WriteAllBytes(System.IO.Path.Combine(sub, "深层.pdf"), [1, 2, 3]);

        var result = _scanner.Scan(new ScanOptions { RootDirectory = _dir.Path, MaxDepth = 1 });

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public void Scan_IncludeGlobs_FiltersByName()
    {
        _dir.CreateFile("a.pdf");
        _dir.CreateFile("b.docx");
        _dir.CreateFile("c.png");

        var result = _scanner.Scan(new ScanOptions
        {
            RootDirectory = _dir.Path,
            IncludeGlobs = ["*.pdf", "*.png"]
        });

        Assert.Equal(["a.pdf", "c.png"], result.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void Scan_ExcludeGlobs_SkipsTempFiles()
    {
        _dir.CreateFile("~$临时.docx");
        _dir.CreateFile("正常.docx");

        var result = _scanner.Scan(new ScanOptions
        {
            RootDirectory = _dir.Path,
            ExcludeGlobs = ["~$*"]
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("正常.docx", item.Name);
    }

    [Fact]
    public void Scan_SizeFilter_Bounds()
    {
        _dir.CreateFile("tiny.pdf", 10);
        _dir.CreateFile("mid.pdf", 1000);
        _dir.CreateFile("huge.pdf", 10_000);

        var result = _scanner.Scan(new ScanOptions
        {
            RootDirectory = _dir.Path,
            MinSizeBytes = 100,
            MaxSizeBytes = 5000
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("mid.pdf", item.Name);
    }

    [Fact]
    public void Scan_ModifiedTimeFilter_Windows()
    {
        var oldFile = _dir.CreateFile("旧.pdf", 10);
        var newFile = _dir.CreateFile("新.pdf", 10);
        File.SetLastWriteTime(oldFile, new DateTime(2020, 1, 1));

        var result = _scanner.Scan(new ScanOptions
        {
            RootDirectory = _dir.Path,
            ModifiedAfter = new DateTime(2024, 1, 1)
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("新.pdf", item.Name);
    }

    [Fact]
    public void Scan_ResultSortedByName_AndExtensionLowercased()
    {
        _dir.CreateFile("B.pdf");
        _dir.CreateFile("A.DOCX");
        _dir.CreateFile("a.pdf");

        var result = _scanner.Scan(new ScanOptions { RootDirectory = _dir.Path });

        Assert.Equal(["A.DOCX", "a.pdf", "B.pdf"], result.Items.Select(i => i.Name).ToArray());
        Assert.All(result.Items, i => Assert.Matches(@"^\.[a-z0-9]+$", i.Extension));
    }

    [Fact]
    public void Scan_NonexistentRoot_ReturnsEmpty()
    {
        var result = _scanner.Scan(new ScanOptions { RootDirectory = @"Z:\不存在\目录" });
        Assert.Empty(result.Items);
    }

    public void Dispose() => _dir.Dispose();
}
