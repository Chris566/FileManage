using FileManage.Core.Models;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// 测试辅助：FileItem 构造、临时目录、固定时钟。
/// </summary>
public static class TestHelper
{
    public static readonly DateTime DefaultModifiedTime = new(2026, 8, 30, 12, 0, 0);

    public static FileItem Item(
        string name,
        string directory = @"D:\data",
        long size = 100,
        DateTime? modifiedTime = null,
        DateTime? exifDate = null,
        string? hash8 = null)
    {
        return new FileItem
        {
            FullPath = Path.Combine(directory, name),
            Name = name,
            Extension = Path.GetExtension(name).ToLowerInvariant(),
            SizeBytes = size,
            ModifiedTime = modifiedTime ?? DefaultModifiedTime,
            ExifDate = exifDate,
            ContentHash8 = hash8
        };
    }
}

/// <summary>固定时钟，用于 {Date} 等时间变量测试。</summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>测试用临时目录，析构时整体删除。</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FileManageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>在临时目录中创建指定大小的文件。</summary>
    public string CreateFile(string name, long sizeBytes = 10)
    {
        var fullPath = System.IO.Path.Combine(Path, name);
        using var stream = File.Create(fullPath);
        stream.SetLength(sizeBytes);
        return fullPath;
    }

    /// <summary>在临时目录中创建含指定文本内容的文件。</summary>
    public string CreateFileWithContent(string name, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, name);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    /// <summary>读取目录内文件的相对路径 → 内容快照（递归）。</summary>
    public Dictionary<string, string> Snapshot()
    {
        return Directory
            .EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => System.IO.Path.GetRelativePath(Path, f),
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase);
    }

    public string SubDir(string name)
    {
        var fullPath = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }
}
