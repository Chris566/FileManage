using FileManage.Core.Models;

namespace FileManage.Core.Scanning;

/// <summary>
/// 扫描选项（设计文档 §4.1）。
/// </summary>
public sealed record ScanOptions
{
    /// <summary>起始目录。</summary>
    public required string RootDirectory { get; init; }

    /// <summary>递归深度，0 = 仅当前层（对齐旧版行为）。</summary>
    public int MaxDepth { get; init; } = 0;

    /// <summary>包含通配模式（匹配文件名，如 "*.pdf"）；空列表 = 不过滤。</summary>
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];

    /// <summary>排除通配模式（匹配文件名，如 "~$*"）；命中即排除。</summary>
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];

    /// <summary>最小文件大小（字节），null = 不限制。</summary>
    public long? MinSizeBytes { get; init; }

    /// <summary>最大文件大小（字节），null = 不限制。</summary>
    public long? MaxSizeBytes { get; init; }

    /// <summary>修改时间下界（含），null = 不限制。</summary>
    public DateTime? ModifiedAfter { get; init; }

    /// <summary>修改时间上界（含），null = 不限制。</summary>
    public DateTime? ModifiedBefore { get; init; }

    /// <summary>读取照片拍摄时间（EXIF），用于 {ExifDate} 命名与 {ExifYear} 归档。仅对照片扩展名生效。</summary>
    public bool ReadExifDate { get; init; }
}

/// <summary>扫描结果。</summary>
public sealed record ScanResult(IReadOnlyList<FileItem> Items);
