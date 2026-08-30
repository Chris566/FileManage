using FileManage.Core.Abstractions;
using FileManage.Core.Models;

namespace FileManage.Core.Scanning;

/// <summary>
/// 文件扫描器（设计文档 §4.1）：按 ScanOptions 枚举并过滤文件。
/// </summary>
public sealed class FileScanner(IFileSystemService fileSystem, IExifService? exifService = null)
{
    /// <summary>启用 EXIF 读取时受益的照片扩展名。</summary>
    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp", ".heic", ".heif" };

    public ScanResult Scan(ScanOptions options, CancellationToken ct = default)
    {
        var paths = fileSystem.EnumerateFiles(options.RootDirectory, options.MaxDepth, ct);
        var items = new List<FileItem>();

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(path);

            if (options.IncludeGlobs.Count > 0 && !WildcardMatcher.IsMatchAny(name, options.IncludeGlobs))
            {
                continue;
            }

            if (options.ExcludeGlobs.Count > 0 && WildcardMatcher.IsMatchAny(name, options.ExcludeGlobs))
            {
                continue;
            }

            var (sizeBytes, modifiedTime) = fileSystem.GetFileInfo(path);

            if (options.MinSizeBytes is { } min && sizeBytes < min)
            {
                continue;
            }

            if (options.MaxSizeBytes is { } max && sizeBytes > max)
            {
                continue;
            }

            if (options.ModifiedAfter is { } after && modifiedTime < after)
            {
                continue;
            }

            if (options.ModifiedBefore is { } before && modifiedTime > before)
            {
                continue;
            }

            items.Add(new FileItem
            {
                FullPath = path,
                Name = name,
                Extension = Path.GetExtension(name).ToLowerInvariant(),
                SizeBytes = sizeBytes,
                ModifiedTime = modifiedTime,
                ExifDate = options.ReadExifDate
                           && exifService is not null
                           && PhotoExtensions.Contains(Path.GetExtension(name))
                    ? exifService.ReadCaptureDate(path)
                    : null
            });
        }

        // 固定按文件名排序，保证预览序号与执行顺序确定
        items.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return new ScanResult(items);
    }
}
